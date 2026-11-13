using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace ChromaGLS.FerrofluidBundle
{
    // Editor validation entry points request PlayMode: edit-mode teardown diagnostics pollute the
    // run, while the play loop tears objects down naturally. The pending validation survives the
    // domain reload through SessionState so EnteredPlayMode can dispatch the actual checks.
    internal static class FerrofluidValidationRunner
    {
        private const string PendingTypeKey = "ChromaGLS.FerrofluidBundle.PendingValidationType";

        [InitializeOnLoadMethod]
        private static void HookPlayModeState()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        internal static void Run(Type validationType)
        {
            SessionState.SetString(PendingTypeKey, validationType.FullName!);
            if (EditorApplication.isPlaying)
                Execute(validationType);
            else if (!EditorApplication.isPlayingOrWillChangePlaymode)
                EditorApplication.EnterPlaymode();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode)
                return;

            string pending = SessionState.GetString(PendingTypeKey, string.Empty);
            if (string.IsNullOrEmpty(pending))
                return;

            Type validationType = FindValidationType(pending);
            if (validationType != null)
                Execute(validationType);
        }

        private static Type FindValidationType(string name)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type candidate = assembly.GetType(name);
                if (candidate != null)
                    return candidate;
            }

            return null;
        }

        private static void Execute(Type validationType)
        {
            try
            {
                MethodInfo method = validationType.GetMethod(
                    "RunRuntime",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (method == null)
                    throw new Exception($"{validationType.Name} has no parameterless static RunRuntime method.");

                object result = method.Invoke(null, null);
                if (result is IEnumerator frames)
                {
                    GameObject host = new("Ferrofluid frame validation");
                    host.AddComponent<FerrofluidValidationFrameRunner>().Begin(frames, Finish);
                    return;
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception is TargetInvocationException { InnerException: not null } wrapped
                    ? wrapped.InnerException!
                    : exception);
                Finish(1);
                return;
            }

            Finish(0);
        }

        private static void Finish(int code)
        {
            SessionState.EraseString(PendingTypeKey);
            EditorApplication.Exit(code);
        }
    }
}
