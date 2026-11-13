using System;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChromaGLS.FerrofluidBundle
{
    internal static class FerrofluidCursorValidation
    {
        public static void Run() => FerrofluidValidationRunner.Run(typeof(FerrofluidCursorValidation));

        private static void RunRuntime()
        {
            const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Assembly plugin = FerrofluidValidationRuntime.CreateConfig().GetType().Assembly;
            Type inputType = plugin.GetType("ChromaGLS.Settings.FerrofluidMagnetController", true);
            MethodInfo suppress = inputType.GetMethod("SuppressGrabCursor", instance)
                ?? throw new Exception("The ferrofluid cursor suppression is unavailable.");
            ConstructorInfo constructor = inputType.GetConstructors(instance)[0];
            Type pointerType = constructor.GetParameters()[3].ParameterType;
            GameObject pointerObject = new("Cursor validation pointer");
            pointerObject.SetActive(false);
            GameObject cursor = new("Cursor validation dot");
            try
            {
                Component pointer = pointerObject.AddComponent(pointerType);
                pointerType.GetField("_cursorTransform", instance).SetValue(pointer, cursor.transform);
                object input = constructor.Invoke(new object[] { null, null, null, pointer, null, null, false });
                suppress.Invoke(input, new object[] { false });
                if (!cursor.activeSelf)
                    throw new Exception("A normal UI hover hides the virtual cursor.");
                suppress.Invoke(input, new object[] { true });
                if (cursor.activeSelf)
                    throw new Exception("A grabbable ferrofluid hover leaves the virtual cursor visible.");

                // VRPointer refreshes its cursor each input frame. Suppression must not own its visibility afterward.
                cursor.SetActive(true);
                suppress.Invoke(input, new object[] { false });
                if (!cursor.activeSelf)
                    throw new Exception("The cursor remains suppressed after leaving ferrofluid.");
                cursor.SetActive(false);
                suppress.Invoke(input, new object[] { false });
                if (cursor.activeSelf)
                    throw new Exception("A normal hover forces the game's hidden cursor visible.");
            }
            finally
            {
                Object.DestroyImmediate(cursor);
                Object.DestroyImmediate(pointerObject);
            }

            Debug.Log("[FerrofluidCursorValidation] PASS: ferrofluid hides the dot without taking over normal cursor visibility.");
        }
    }
}
