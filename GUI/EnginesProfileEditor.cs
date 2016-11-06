//  Author:
//       Allis Tauri <allista@gmail.com>
//
//  Copyright (c) 2015 Allis Tauri
//
// This work is licensed under the Creative Commons Attribution-ShareAlike 4.0 International License. 
// To view a copy of this license, visit http://creativecommons.org/licenses/by-sa/4.0/ 
// or send a letter to Creative Commons, PO Box 1866, Mountain View, CA 94042, USA.

using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using KSP.UI.Screens;
using AT_Utils;

namespace ThrottleControlledAvionics
{
	[KSPAddon(KSPAddon.Startup.EditorAny, false)]
	public class EnginesProfileEditor : AddonWindowBase<EnginesProfileEditor>
	{
		const string DefaultConstractName = "Untitled Space Craft";

		NamedConfig CFG;
		readonly List<EngineWrapper> Engines = new List<EngineWrapper>();
		float Mass, DryMass, MinTWR, MaxTWR;

		public static bool Available { get; private set; }
		static Dictionary<Type,bool> Modules = new Dictionary<Type, bool>();

		public override void Awake()
		{
			base.Awake();
			width = 600;
			height = 200;
			GameEvents.onEditorShipModified.Add(OnShipModified);
			GameEvents.onEditorLoad.Add(OnShipLoad);
			GameEvents.onEditorRestart.Add(Reset);
			GameEvents.onEditorStarted.Add(Started);
			Available = false;
			//module availability
			TCAModulesDatabase.ValidModules
				.ForEach(t => Modules.Add(t, TCAModulesDatabase.ModuleAvailable(t)));
		}

		public override void OnDestroy ()
		{
			GameEvents.onEditorShipModified.Remove(OnShipModified);
			GameEvents.onEditorLoad.Remove(OnShipLoad);
			GameEvents.onEditorRestart.Remove(Reset);
			GameEvents.onEditorStarted.Remove(Started);
			TCAMacroEditor.Exit();
			base.OnDestroy();
		}

		static void UpdatePartsInfo()
		{
			//update TCA part infos
			var info = TCAScenario.ModuleStatusString();
			foreach(var ap in PartLoader.LoadedPartsList)
			{
				foreach(var mi in ap.moduleInfos)
				{
					if(mi.moduleName != ModuleTCA.TCA_NAME) continue;
					mi.primaryInfo = "<b>TCA:</b> "+info;
					mi.info = info;
				}
			}
		}

		void Started() { UpdatePartsInfo(); }

		void Reset() { reset = true; }

		void OnShipLoad(ShipConstruct ship, CraftBrowserDialog.LoadType load_type)
		{ init_engines = load_type == CraftBrowserDialog.LoadType.Normal; }

		bool GetCFG(ShipConstruct ship)
		{
			var TCA_Modules = ModuleTCA.AllTCA(ship);
			if(TCA_Modules.Count == 0) { Reset(); return false; }
			CFG = null;
			foreach(var tca in TCA_Modules)
			{
				if(tca.CFG == null) continue;
				CFG = NamedConfig.FromVesselConfig(ship.shipName, tca.CFG);
				break;
			}
			if(CFG == null)
			{
				CFG = new NamedConfig(ship.shipName);
				CFG.EnginesProfiles.AddProfile(Engines);
			}
			else CFG.ActiveProfile.Apply(Engines);
			CFG.ActiveProfile.Update(Engines);
			UpdateCFG(TCA_Modules);
			return true;
		}

		void UpdateCFG(IList<ModuleTCA> TCA_Modules)
		{
			if(CFG == null || TCA_Modules.Count == 0) return;
			TCA_Modules.ForEach(m => m.CFG = null);
			TCA_Modules[0].CFG = CFG;
		}
		void UpdateCFG(ShipConstruct ship)
		{ UpdateCFG(ModuleTCA.AllTCA(ship)); }

		bool UpdateEngines(ShipConstruct ship)
		{
			Engines.Clear();
			var thrust = Vector3.zero;
			Mass = DryMass = MinTWR = MaxTWR = 0f;
			ship.GetShipMass(out DryMass, out Mass);
			Mass += DryMass;
			if(TCAScenario.HasTCA && ship.Parts != null) 
			{ 
				(from p in ship.Parts where p.Modules != null
				 from m in p.Modules.GetModules<ModuleEngines>() select m)
					.ForEach(m =>
					{
						var e = new EngineWrapper(m);
						Engines.Add(e);
						e.UpdateThrustInfo();
						if(CFG != null)
						{
							var ecfg = CFG.ActiveProfile.GetConfig(e);
							if(ecfg == null || ecfg.On)
								thrust += e.wThrustDir*e.thrustInfo.thrust;
						}
					});
				var T = thrust.magnitude/Utils.G0;
				MinTWR = T/Mass;
				MaxTWR = T/DryMass;
			}
			var ret = Engines.Count > 0;
			if(!ret) Reset();
			return ret;
		}

		void OnShipModified(ShipConstruct ship) { update_engines = true; }

		bool update_engines, init_engines, reset;
		void Update()
		{
			if(EditorLogic.fetch == null) return;
			if(reset)
			{
				Available = false;
				Engines.Clear();
				CFG = null;
				reset = false;
			}
			if(init_engines)
			{
				if(UpdateEngines(EditorLogic.fetch.ship))
					GetCFG(EditorLogic.fetch.ship);
				init_engines = false;
			}
			if(update_engines)
			{
				if(UpdateEngines(EditorLogic.fetch.ship))
				{
					if(CFG != null) UpdateCFG(EditorLogic.fetch.ship);
					else GetCFG(EditorLogic.fetch.ship);
					if(CFG != null) CFG.ActiveProfile.Update(Engines);
				}
				update_engines = false;
			}
			Available |= CFG != null && Engines.Count > 0;
			if(Available) CFG.GUIVisible = CFG.Enabled;
		}

		void DrawMainWindow(int windowID)
		{
			//help button
			if(GUI.Button(new Rect(WindowPos.width - 23f, 2f, 20f, 18f), 
			              new GUIContent("?", "Help"))) TCAManual.Toggle();
			GUILayout.BeginVertical();
			if(Modules[typeof(MacroProcessor)])
				{
					if(TCAMacroEditor.Editing)
						GUILayout.Label("Edit Macros", Styles.inactive_button, GUILayout.ExpandWidth(true));
					else if(GUILayout.Button("Edit Macros", Styles.normal_button, GUILayout.ExpandWidth(true)))
						TCAMacroEditor.Edit(CFG);
				}
				GUILayout.BeginHorizontal();
					GUILayout.BeginVertical();
						GUILayout.BeginHorizontal();
							Utils.ButtonSwitch("Enable TCA", ref CFG.Enabled, "", GUILayout.ExpandWidth(false));
							if(Modules[typeof(AltitudeControl)])
							{
								if(Utils.ButtonSwitch("Hover", CFG.VF[VFlight.AltitudeControl], 
				                                      "Enable Altitude Control", GUILayout.ExpandWidth(false)))
									CFG.VF.Toggle(VFlight.AltitudeControl);
								Utils.ButtonSwitch("Follow Terrain", ref CFG.AltitudeAboveTerrain, 
				                                   "Enable follow terrain mode", GUILayout.ExpandWidth(false));
							}
							if(Modules[typeof(VTOLControl)])
							{
								if(Utils.ButtonSwitch("VTOL Mode", CFG.CTRL[ControlMode.VTOL], 
				                                      "Keyboard controls thrust direction instead of torque", GUILayout.ExpandWidth(false)))
									CFG.CTRL.XToggle(ControlMode.VTOL);
							}
							if(Modules[typeof(VTOLAssist)])
								Utils.ButtonSwitch("VTOL Assist", ref CFG.VTOLAssistON, 
				                                   "Automatic assistnce with vertical takeof or landing", GUILayout.ExpandWidth(false));
							if(Modules[typeof(FlightStabilizer)])
								Utils.ButtonSwitch("Flight Stabilizer", ref CFG.StabilizeFlight, 
				                                   "Automatic flight stabilization when vessel is out of control", GUILayout.ExpandWidth(false));
							if(Modules[typeof(HorizontalSpeedControl)])
								Utils.ButtonSwitch("H-Translation", ref CFG.CorrectWithTranslation, 
				                                   "Use translation to correct horizontal velocity", GUILayout.ExpandWidth(false));
							if(Modules[typeof(CollisionPreventionSystem)]) 
								Utils.ButtonSwitch("CPS", ref CFG.UseCPS, 
				                                   "Enable Collistion Prevention System", GUILayout.ExpandWidth(false));
						GUILayout.EndHorizontal();
						GUILayout.BeginHorizontal();
							Utils.ButtonSwitch("AutoThrottle", ref CFG.BlockThrottle, 
			                                   "Change altitude/vertical velocity using main throttle control", GUILayout.ExpandWidth(true));
							Utils.ButtonSwitch("AutoGear", ref CFG.AutoGear, 
			                                   "Automatically deploy/retract landing gear when needed", GUILayout.ExpandWidth(true));
							Utils.ButtonSwitch("AutoBrakes", ref CFG.AutoBrakes, 
							                   "Automatically ebable/disable brakes when needed", GUILayout.ExpandWidth(true));
							Utils.ButtonSwitch("AutoStage", ref CFG.AutoStage, 
							                   "Automatically activate next stage when previous falmeouted", GUILayout.ExpandWidth(true));
							Utils.ButtonSwitch("AutoChute", ref CFG.AutoParachutes, 
							                   "Automatically activate parachutes when needed", GUILayout.ExpandWidth(true));
						GUILayout.EndHorizontal();
					GUILayout.EndVertical();
				GUILayout.EndHorizontal();
				CFG.EnginesProfiles.Draw(height);
				if(CFG.ActiveProfile.Changed)
				{
					CFG.ActiveProfile.Apply(Engines);
					update_engines = true;
				}
				GUILayout.BeginHorizontal(Styles.white);
					GUILayout.Label("Ship Info:");
					GUILayout.FlexibleSpace();
					GUILayout.Label(string.Format("Mass: {0} ► {1}", Utils.formatMass(Mass), Utils.formatMass(DryMass)), Styles.boxed_label);
					GUILayout.Label(string.Format("TWR: {0:F2} ► {1:F2}", MinTWR, MaxTWR), Styles.boxed_label);
				GUILayout.EndHorizontal();
			GUILayout.EndVertical();
			TooltipsAndDragWindow(WindowPos);
		}

		protected override bool can_draw()
		{ return Engines.Count > 0 && CFG != null; }

		protected override void draw_gui()
		{
			LockControls();
			WindowPos = 
				GUILayout.Window(GetInstanceID(), 
				                 WindowPos, 
				                 DrawMainWindow, 
				                 Title,
				                 GUILayout.Width(width),
				                 GUILayout.Height(height)).clampToScreen();
		}
	}
}