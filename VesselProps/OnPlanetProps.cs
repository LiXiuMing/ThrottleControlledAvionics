﻿//  Author:
//       Allis Tauri <allista@gmail.com>
//
//  Copyright (c) 2016 Allis Tauri
//
// This work is licensed under the Creative Commons Attribution-ShareAlike 4.0 International License. 
// To view a copy of this license, visit http://creativecommons.org/licenses/by-sa/4.0/ 
// or send a letter to Creative Commons, PO Box 1866, Mountain View, CA 94042, USA.
//
using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using ModuleWheels;
using AT_Utils;

namespace ThrottleControlledAvionics
{

	public class OnPlanetProps : VesselProps
	{
		public OnPlanetProps(VesselWrapper vsl) : base(vsl) {}

		public List<ModuleParachute> Parachutes = new List<ModuleParachute>();
		public List<ModuleParachute> UnusedParachutes = new List<ModuleParachute>();
		public List<ModuleWheelDeployment> LandingGear = new List<ModuleWheelDeployment>();
		public bool HaveParachutes { get; private set; }
		public bool HaveUsableParachutes { get; private set; }
		public bool ParachutesActive { get; private set; }
		public bool ParachutesDeployed { get; private set; }
		public int  NearestParachuteStage { get; private set; }
		readonly ActionDamper parachute_cooldown = new ActionDamper(0.5);

		public Vector3 Fwd { get; private set; }  //fwd unit vector of the Control module in world space
		public Vector3 FwdL { get; private set; }  //fwd unit vector of the Control module in local space
		public bool    NoseUp { get; private set; }  //if the forward is refT.forward or refT.up
		public Vector3 Heading { get; private set; }  //bearing unit vector of the Control module in world space

		public float   MaxTWR { get; private set; }
		public float   MaxDTWR { get; private set; }
		public float   DTWR { get; private set; }
		public float   DTWRf { get { return DTWR_filter.Value; } }
		LowPassFilterF DTWR_filter = new LowPassFilterF();

		public float   CurrentThrustAccelerationTime { get; private set; }
		public float   CurrentThrustDecelerationTime { get; private set; }

		public float   VSF; //vertical speed factor
		public float   GeeVSF; //the value of VSF that provides the thrust equal to the gravity force
		public float   MinVSF; //minimum allowable VSF to have enough control authority

		public float   TWRf { get; private set; } //all VSC engines should be pointed down when this is low: [1e-9, 1]

		public bool    HaveLandingGear { get; private set; }
		public float   GearDeployTime { get; private set; }

		public bool    GearDeploying
		{ get { return LandingGear.Count > 0 && LandingGear.All(g => g.fsm.CurrentState == g.st_deploying); } }

		public bool    GearDeployed
		{ get { return LandingGear.Count == 0 || LandingGear.All(g => g.fsm.CurrentState == g.st_deployed); } }

		public bool    GearRetracting
		{ get { return LandingGear.Count > 0 && LandingGear.All(g => g.fsm.CurrentState == g.st_retracting); } }

		public bool    GearRetracted
		{ get { return LandingGear.Count == 0 || LandingGear.All(g => g.fsm.CurrentState == g.st_retracted); } }

		public bool AddGear(PartModule pm)
		{
			var wheel = pm as ModuleWheelDeployment;
			if(wheel != null)
			{
				var animator = wheel.part.FindModelAnimator(wheel.animationTrfName, wheel.animationStateName);
				if(animator != null)
				{
					LandingGear.Add(wheel);
					GearDeployTime = Mathf.Max(GearDeployTime, animator.clip.length);
					HaveLandingGear = true;
				}
				return true;
			}
			return false;
		}

		public override void Clear()
		{
			HaveLandingGear = false;
			GearDeployTime = 0;
			LandingGear.Clear();
			Parachutes.Clear();
		}

		public override void Update()
		{
			CurrentThrustAccelerationTime = 0f; CurrentThrustDecelerationTime = 0f; TWRf = 1;
			//calculate total downward thrust and slow engines' corrections
			MaxTWR  = VSL.Engines.MaxThrustM/VSL.Physics.mg;
			DTWR = Vector3.Dot(VSL.Engines.Thrust, VSL.Physics.Up) < 0? 
				Vector3.Project(VSL.Engines.Thrust, VSL.Physics.Up).magnitude/VSL.Physics.mg : 0f;
			DTWR_filter.Tau = Mathf.Max(VSL.Engines.AccelerationTime, VSL.Engines.DecelerationTime);
			DTWR_filter.Update(DTWR);
			GeeVSF = 1/Utils.ClampL(MaxTWR, 1);
			var mVSFtor = (VSL.Torque.MaxPitchRoll.AA_rad > 0)? 
				Utils.ClampH(GLB.VSC.MinVSFf/VSL.Torque.MaxPitchRoll.AA_rad, GLB.VSC.MaxVSFtwr*GeeVSF) : 0;
			MinVSF = Mathf.Lerp(0, mVSFtor, Mathf.Pow(VSL.Controls.Steering.sqrMagnitude, 0.25f));
			var down_thrust = 0f;
			var slow_thrust = 0f;
			var fast_thrust = 0f;
			for(int i = 0; i < VSL.Engines.NumActive; i++)
			{
				var e = VSL.Engines.Active[i];
				e.VSF = 1;
				if(e.thrustInfo == null) continue;
				if(e.isVSC)
				{
					var dcomponent = -Vector3.Dot(e.wThrustDir, VSL.Physics.Up);
					if(dcomponent <= 0) e.VSF = VSL.HasUserInput? 0 : GeeVSF*VSL.Controls.InvAlignmentFactor;
					else 
					{
						var dthrust = e.nominalCurrentThrust(e.best_limit)*dcomponent;
						if(e.useEngineResponseTime && dthrust > 0) 
						{
							slow_thrust += dthrust;
							CurrentThrustAccelerationTime += e.engineAccelerationSpeed*dthrust;
							CurrentThrustDecelerationTime += e.engineDecelerationSpeed*dthrust;
						}
						else fast_thrust = dthrust;
						down_thrust += dthrust;
					}
				}
			}
			MaxDTWR = Utils.EWA(MaxDTWR, down_thrust/VSL.Physics.mg, 0.1f);
			if(refT != null)
			{
				Fwd = Vector3.Cross(VSL.refT.right, -VSL.Engines.MaxThrust).normalized;
				FwdL = refT.InverseTransformDirection(Fwd);
				NoseUp = Mathf.Abs(Vector3.Dot(Fwd, VSL.refT.forward)) >= 0.9;
				Heading = Vector3.ProjectOnPlane(Fwd, VSL.Physics.Up).normalized;
			}
			var controllable_thrust = slow_thrust+fast_thrust;
			if(controllable_thrust > 0)
			{
				//correct setpoint for current TWR and slow engines
				var rel_slow_thrust = slow_thrust*slow_thrust/controllable_thrust;
				if(CurrentThrustAccelerationTime > 0) CurrentThrustAccelerationTime = rel_slow_thrust/CurrentThrustAccelerationTime*GLB.VSC.ASf;
				if(CurrentThrustDecelerationTime > 0) CurrentThrustDecelerationTime = rel_slow_thrust/CurrentThrustDecelerationTime*GLB.VSC.DSf;
				//TWR factor
				var vsf = CFG.VSCIsActive && VSL.VerticalSpeed.Absolute < 0? 
					Utils.Clamp(1-(Utils.ClampH(CFG.VerticalCutoff, 0)-VSL.VerticalSpeed.Absolute)/GLB.TDC.VSf, 1e-9f, 1) : 1;
				var twr = VSL.Engines.Slow? DTWR_filter.Value : MaxTWR*Utils.Sin45; //MaxTWR at 45deg
				TWRf = Utils.Clamp(twr/GLB.TDC.TWRf, 1e-9f, 1)*vsf;
			}
			//parachutes
			UnusedParachutes.Clear();
			ParachutesActive = false;
			ParachutesDeployed = false;
			NearestParachuteStage = 0;
			for(int i = 0, count = Parachutes.Count; i < count; i++)
			{
				var p = Parachutes[i];
				if(p.part.inverseStage > NearestParachuteStage) 
					NearestParachuteStage = p.part.inverseStage;
				ParachutesActive |= 
					p.deploymentState == ModuleParachute.deploymentStates.ACTIVE ||
					p.deploymentState == ModuleParachute.deploymentStates.SEMIDEPLOYED ||
					p.deploymentState == ModuleParachute.deploymentStates.DEPLOYED;
				ParachutesDeployed |= p.deploymentState == ModuleParachute.deploymentStates.DEPLOYED && p.part.maximum_drag/p.fullyDeployedDrag > 0.9;
				if(p.part != null && !p.part.ShieldedFromAirstream && p.deploymentState == ModuleParachute.deploymentStates.STOWED)
					UnusedParachutes.Add(p);
			}
			HaveUsableParachutes = UnusedParachutes.Count > 0;
			HaveParachutes = HaveUsableParachutes || ParachutesActive;
		}

		public void ActivateParachutes()
		{
			if(!CFG.AutoParachutes) return;
			for(int i = 0, count = UnusedParachutes.Count; i < count; i++)
			{
				var p = Parachutes[i];
				if(p.deploymentSafeState == ModuleParachute.deploymentSafeStates.SAFE &&
				   VSL.Altitude.Relative < p.deployAltitude
				   +Mathf.Abs(VSL.VerticalSpeed.Absolute)*(1/Utils.ClampL(p.semiDeploymentSpeed, 0.1f)+10))
					parachute_cooldown.Run(p.Deploy);
			}
		}
	}
}

