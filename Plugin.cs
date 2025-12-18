using Audial.Utils;
using BepInEx;
using BepInEx.Logging;
using MonoMod.RuntimeDetour;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace WeaponLasers
{

	[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
	public class Plugin : BaseUnityPlugin
	{
		internal static new ManualLogSource Logger;
		private static Hook AwakeHook;
		private static Hook DestroyHook;
		internal static List<PhysGrabObject> CatchedPhysGrabObjects = new List<PhysGrabObject>();

		private void Awake()
		{
			Logger = base.Logger;
			// Create a GameObject to host our renderer
			GameObject go = new GameObject("WeaponLasersRenderer");
			go.hideFlags = HideFlags.HideAndDontSave;
			go.AddComponent<LaserRenderer>();
			DontDestroyOnLoad(go);
			PhysGrabObject[] array = UnityEngine.Object.FindObjectsOfType<PhysGrabObject>(true);
			List<PhysGrabObject> list = new List<PhysGrabObject>(array.Length);
			foreach (PhysGrabObject physGrabObject in array)
			{
				list.Add(physGrabObject);
			}
			Plugin.CatchedPhysGrabObjects = list;
			// Install detours
			AwakeHook = new Hook(
				typeof(PhysGrabObject).GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic),
				typeof(Plugin).GetMethod(nameof(Awake_Detour), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
			);

			DestroyHook = new Hook(
				typeof(PhysGrabObject).GetMethod("OnDestroy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic),
				typeof(Plugin).GetMethod(nameof(OnDestroy_Detour), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
			);
			Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
		}

		// -----------------------------
		// Detours
		// -----------------------------

		private static void Awake_Detour(Action<PhysGrabObject> orig, PhysGrabObject self)
		{
			orig(self); // call original Awake
			CatchedPhysGrabObjects.Add(self);
		}

		private static void OnDestroy_Detour(Action<PhysGrabObject> orig, PhysGrabObject self)
		{
			orig(self); // call original OnDestroy
			CatchedPhysGrabObjects.Remove(self);
		}

		private class LaserRenderer : MonoBehaviour
		{
			private void OnGUI()
			{
				try
				{
					foreach (PhysGrabObject physGrabObject in Plugin.CatchedPhysGrabObjects)
					{
						try
						{
							if (physGrabObject != null && physGrabObject.grabbedLocal && physGrabObject.GetField<bool>("isActive"))
							{
								bool isGun = physGrabObject.GetField<bool>("isGun");
								if (isGun)
								{
									ItemGun itemGun = physGrabObject.GetComponent<ItemGun>();
									if (itemGun != null && itemGun.gunMuzzle != null)
									{
										Vector3 barrelPosition = itemGun.gunMuzzle.position;
										Vector3 barrelDirection = itemGun.gunMuzzle.forward;

										// Calculate the end point of the laser (e.g., 100 units forward)
										float laserDistance = 100f;
										Vector3 laserEndPoint = barrelPosition + (barrelDirection * laserDistance);

										// Check if camera exists
										if (Camera.main == null) continue;

										// Convert world positions to screen positions
										Vector3 screenStart;
										Vector3 screenEnd;
										if (Utils.WorldToScreen(Camera.main, barrelPosition, out screenStart) &&
											Utils.WorldToScreen(Camera.main, laserEndPoint, out screenEnd))
										{
											// Draw the laser line
											Render.Line(
												new Vector2(screenStart.x, screenStart.y),
												new Vector2(screenEnd.x, screenEnd.y),
												2f,
												Color.red
											);
										}
									}
								}
							}
						}
						catch (Exception ex)
						{
							Logger.LogError($"Inner loop error: {ex}");
						}
					}
				}
				catch (Exception ex)
				{
					Logger.LogError($"OnGUI error: {ex}");
				}
			}
		}
	}


	// Reflection Extension Methods
	internal static class ReflectExtension
	{
		private static Dictionary<string, FieldInfo> _fieldCache = new Dictionary<string, FieldInfo>();

		internal static T GetField<T>(this object obj, string name)
		{
			string text = obj.GetType().FullName + ":" + name;
			FieldInfo fieldInfo;
			bool flag = !ReflectExtension._fieldCache.TryGetValue(text, out fieldInfo);
			if (flag)
			{
				FieldInfo field = obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic);
				if (field == null)
				{
					throw new MissingFieldException("Field '" + name + "' not found");
				}
				fieldInfo = field;
				ReflectExtension._fieldCache[text] = fieldInfo;
			}
			return (T)((object)fieldInfo.GetValue(obj));
		}
	}

	// Utility class for screen conversions
	internal static class Utils
	{
		internal static bool WorldToScreen(Camera camera, Vector3 world, out Vector3 screen)
		{
			screen = camera.WorldToViewportPoint(world);
			screen.x *= (float)Screen.width;
			screen.y *= (float)Screen.height;
			screen.y = (float)Screen.height - screen.y;
			return screen.z > 0f;
		}
	}

	// Rendering utility class
	internal static class Render
	{
		internal static Color Color = Color.white;

		internal static void Line(Vector2 from, Vector2 to, float thickness, Color color)
		{
			Render.Color = color;
			Render.Line(from, to, thickness);
		}

		internal static void Line(Vector2 from, Vector2 to, float thickness)
		{
			Vector2 normalized = (to - from).normalized;
			float angle = Mathf.Atan2(normalized.y, normalized.x) * 57.29578f;
			GUIUtility.RotateAroundPivot(angle, from);
			Render.Box(from, Vector2.right * (from - to).magnitude, thickness, false);
			GUIUtility.RotateAroundPivot(-angle, from);
		}

		internal static void Box(Vector2 position, Vector2 size, float thickness, Color color, bool centered = true)
		{
			Render.Color = color;
			Render.Box(position, size, thickness, centered);
		}

		internal static void Box(Vector2 position, Vector2 size, float thickness, bool centered = true)
		{
			Vector2 vector = centered ? (position - size / 2f) : position;
			GUI.color = Render.Color;
			GUI.DrawTexture(new Rect(vector.x, vector.y, size.x, thickness), Texture2D.whiteTexture);
			GUI.DrawTexture(new Rect(vector.x, vector.y, thickness, size.y), Texture2D.whiteTexture);
			GUI.DrawTexture(new Rect(vector.x + size.x, vector.y, thickness, size.y), Texture2D.whiteTexture);
			GUI.DrawTexture(new Rect(vector.x, vector.y + size.y, size.x + thickness, thickness), Texture2D.whiteTexture);
			GUI.color = Color.white;
		}
	}
}