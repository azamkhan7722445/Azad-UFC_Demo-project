using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace Unity.AI.Assistant.PlayModeTest
{
    [InitializeOnLoad]
    internal static class PlayModeTestRunner
    {
        private const string StateKey = "PlayModeTest.State";
        private const string ResultKey = "PlayModeTest.Result";
        private const string ScriptPathKey = "PlayModeTest.ScriptPath";
        private const string SentinelLog = "PLAY_MODE_TEST_COMPLETE";

        private static readonly int WaitFrames = SessionState.GetInt("PlayModeTest.WaitFrames", 120);

        private static List<string> _capturedLogs = new List<string>();
        private const int MaxCapturedLogs = 150;

        static PlayModeTestRunner()
        {
            string state = SessionState.GetString(StateKey, "Idle");

            switch (state)
            {
                case "Idle":
                    break;

                case "WaitingForCompile":
                    Debug.Log("[PlayModeTest] Bootstrap compiled. Scheduling Play Mode entry.");
                    EditorApplication.delayCall += () =>
                    {
                        SessionState.SetString(StateKey, "EnteringPlayMode");
                        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
                        EditorApplication.isPlaying = true;
                    };
                    break;

                case "EnteringPlayMode":
                    EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
                    if (EditorApplication.isPlaying)
                    {
                        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
                        SessionState.SetString(StateKey, "InPlayMode");
                        _capturedLogs.Clear();
                        Application.logMessageReceived += OnLogMessage;
                        EditorApplication.update += WaitFramesThenRun;
                    }
                    break;

                case "InPlayMode":
                    if (EditorApplication.isPlaying)
                    {
                        Application.logMessageReceived += OnLogMessage;
                        EditorApplication.update += WaitFramesThenRun;
                    }
                    break;

                case "Done":
                    Debug.Log(SentinelLog);
                    EditorApplication.delayCall += SelfDestruct;
                    break;
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
                SessionState.SetString(StateKey, "InPlayMode");
                _capturedLogs.Clear();
                Application.logMessageReceived += OnLogMessage;
                EditorApplication.update += WaitFramesThenRun;
            }
        }

        private static int _frameCount = 0;
        private static bool _hasRun = false;

        private static void WaitFramesThenRun()
        {
            _frameCount++;
            if (_frameCount < WaitFrames) return;

            if (_hasRun) return;
            _hasRun = true;
            EditorApplication.update -= WaitFramesThenRun;

            string resultJson;
            try
            {
                resultJson = RunTestLogic();
            }
            catch (System.Exception e)
            {
                Debug.LogError("[PlayModeTest] Test threw exception: " + e);
                resultJson = JsonUtility.ToJson(new TestResult
                {
                    success = false,
                    error = e.Message,
                    logs = _capturedLogs.ToArray()
                });
            }
            finally
            {
                Application.logMessageReceived -= OnLogMessage;
            }

            SessionState.SetString(ResultKey, resultJson);
            SessionState.SetString(StateKey, "Done");
            EditorApplication.isPlaying = false;
        }

        private static void SelfDestruct()
        {
            string scriptPath = SessionState.GetString(ScriptPathKey, "");
            if (!string.IsNullOrEmpty(scriptPath) && AssetDatabase.AssetPathExists(scriptPath))
            {
                AssetDatabase.DeleteAsset(scriptPath);
            }
            SessionState.EraseString(StateKey);
            SessionState.EraseString(ScriptPathKey);
        }

        private static void OnLogMessage(string message, string stackTrace, LogType type)
        {
            if (_capturedLogs.Count >= MaxCapturedLogs) return;
            _capturedLogs.Add("[" + type + "] " + message + (type == LogType.Exception || type == LogType.Error ? "\n" + stackTrace : ""));
        }

        [System.Serializable]
        private class TestResult
        {
            public bool success;
            public string error;
            public string[] logs;
            public string fileName;
            public int handle;
            public bool modelEnabled;
            public string modelPosition;
        }

        private static string RunTestLogic()
        {
            var arena = GameObject.Find("UFC Arena");
            bool arenaFound = arena != null;
            string fileName = "";
            int handleValue = -2;
            bool modelEnabled = false;
            string posStr = "";

            if (arenaFound)
            {
                var comp = arena.GetComponent("GraciaSplatsModel");
                if (comp != null)
                {
                    var mono = comp as MonoBehaviour;
                    modelEnabled = mono.enabled;
                    posStr = arena.transform.position.ToString();

                    var so = new SerializedObject(comp);
                    var prop = so.FindProperty("fileName");
                    if (prop != null)
                    {
                        fileName = prop.stringValue;
                    }

                    var handleField = comp.GetType().GetField("handle", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if (handleField != null)
                    {
                        handleValue = (int)handleField.GetValue(comp);
                    }
                }
            }

            return JsonUtility.ToJson(new TestResult
            {
                success = true,
                error = arenaFound ? "" : "UFC Arena GameObject not found",
                logs = _capturedLogs.ToArray(),
                fileName = fileName,
                handle = handleValue,
                modelEnabled = modelEnabled,
                modelPosition = posStr
            });
        }
    }
}