﻿//   ControlWindow.cs
//
//  Author:
//       Allis Tauri <allista@gmail.com>
//
//  Copyright (c) 2017 Allis Tauri

using AT_Utils;
using UnityEngine;

namespace ThrottleControlledAvionics
{
	public abstract class ControlWindow : FloatingWindow
	{
		public ModuleTCA TCA;
		public VesselWrapper VSL { get { return TCA.VSL; } }
		internal static Globals GLB { get { return Globals.Instance; } }
		public VesselConfig CFG { get { return TCA.VSL.CFG; } }

		public virtual void Reset() 
		{
			TCA = null;
			ModuleTCA.ResetModuleFields(this);
			foreach(var sw in subwindows)
			{
				ModuleTCA.ResetModuleFields(sw);
				ModuleTCA.SetTCAField(sw, null);
			}
		}

		public virtual void Init(ModuleTCA tca) 
		{ 
			TCA = tca; 
			TCA.InitModuleFields(this);
			foreach(var sw in subwindows)
			{
				TCA.InitModuleFields(sw);
				TCA.SetTCAField(sw);
			}
		}
	}

	//Oh, mixins, where are you?!
	public abstract class ControlSubwindow : SubWindow
	{
		public ModuleTCA TCA;
		public VesselWrapper VSL { get { return TCA.VSL; } }
		internal static Globals GLB { get { return Globals.Instance; } }
		public VesselConfig CFG { get { return TCA.VSL.CFG; } }
	}

	public class ControlDialog : GUIWindowBase
	{
		public ModuleTCA TCA;
		public VesselWrapper VSL { get { return TCA.VSL; } }
		internal static Globals GLB { get { return Globals.Instance; } }
		public VesselConfig CFG { get { return TCA.VSL.CFG; } }

		protected string Title = "Options";
		protected virtual void MainWindow(int windowID) { }

		public void Draw()
		{
			if(doShow && can_draw())
			{
				LockControls();
				WindowPos = 
					GUILayout.Window(GetInstanceID(), 
					                     WindowPos, 
					                     MainWindow, 
					                     Title,
					                     GUILayout.Width(width),
					                     GUILayout.Height(height)).clampToScreen();
			}
			else UnlockControls();
		}
	}
}

