using System;
using UnityEditor;
using UnityEngine;

namespace UGS.UnitTask.Editor
{
    public sealed class UnitTaskDebugWindow : EditorWindow
    {
        private int _selectedSourceIndex;
        private Vector2 _scroll;
        private int _selectedChainIndex;
        private bool _showDecisions;
        private int _decisionsToShow;

        [MenuItem("UGS/UnitTask/Debug Window")]
        public static void Open()
        {
            var window = GetWindow<UnitTaskDebugWindow>();
            window.titleContent = new GUIContent("UGS.UnitTask");
            window.Show();
        }

        private void OnEnable()
        {
            _selectedSourceIndex = 0;
            _selectedChainIndex = 0;
            _showDecisions = true;
            _decisionsToShow = 64;
        }

        private void OnInspectorUpdate()
        {
            if (EditorApplication.isPlaying)
            {
                Repaint();
            }
        }

        private void OnGUI()
        {
            var sources = UnitTaskDebugRegistry.GetSources();
            DrawHeader(sources);

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("进入 Play Mode 后显示实时快照。", MessageType.Info);
                return;
            }

            if (sources.Length == 0)
            {
                EditorGUILayout.HelpBox("未发现调试源。启用调试追踪后 UnitTaskScheduler 会自动注册，或手动调用 UnitTaskDebugRegistry.Register(source)。", MessageType.Warning);
                return;
            }

            if (_selectedSourceIndex < 0 || _selectedSourceIndex >= sources.Length)
            {
                _selectedSourceIndex = 0;
            }

            var source = sources[_selectedSourceIndex];
            var snapshot = SafeCapture(source);
            if (snapshot == null)
            {
                EditorGUILayout.HelpBox("Capture() 失败。", MessageType.Error);
                return;
            }

            DrawBody(snapshot);
        }

        private void DrawHeader(IUnitTaskDebugSnapshotSource[] sources)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var label = sources.Length == 0 ? "No Sources" : sources.Length.ToString();
                GUILayout.Label($"Sources: {label}", EditorStyles.toolbarButton);

                GUILayout.FlexibleSpace();

                var names = new string[sources.Length];
                for (var i = 0; i < sources.Length; i++)
                {
                    names[i] = FormatSourceName(sources[i]);
                }

                if (names.Length > 0)
                {
                    _selectedSourceIndex = EditorGUILayout.Popup(_selectedSourceIndex, names, EditorStyles.toolbarPopup, GUILayout.Width(260f));
                }
            }
        }

        private void DrawBody(UnitTaskSchedulerSnapshot snapshot)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(260f)))
                {
                    GUILayout.Label($"Time: {snapshot.Time:0.000}", EditorStyles.boldLabel);
                    GUILayout.Space(4f);
                    DrawChainsList(snapshot);
                }

                using (new EditorGUILayout.VerticalScope())
                {
                    _scroll = EditorGUILayout.BeginScrollView(_scroll);
                    DrawChainDetails(snapshot);
                    GUILayout.Space(8f);
                    DrawDecisions(snapshot);
                    EditorGUILayout.EndScrollView();
                }
            }
        }

        private void DrawChainsList(UnitTaskSchedulerSnapshot snapshot)
        {
            var chains = snapshot.Chains;
            if (chains == null || chains.Count == 0)
            {
                EditorGUILayout.HelpBox("当前没有任务链。", MessageType.Info);
                return;
            }

            if (_selectedChainIndex < 0 || _selectedChainIndex >= chains.Count)
            {
                _selectedChainIndex = 0;
            }

            for (var i = 0; i < chains.Count; i++)
            {
                var chain = chains[i];
                var text = $"{chain.Name} (#{chain.ChainId}) P{chain.Priority} {chain.Status}";
                var isSelected = i == _selectedChainIndex;
                var style = isSelected ? EditorStyles.miniButtonMid : EditorStyles.miniButton;
                if (GUILayout.Button(text, style))
                {
                    _selectedChainIndex = i;
                }
            }
        }

        private void DrawChainDetails(UnitTaskSchedulerSnapshot snapshot)
        {
            var chains = snapshot.Chains;
            if (chains == null || chains.Count == 0)
            {
                return;
            }

            var chain = chains[_selectedChainIndex];
            GUILayout.Label(chain.Name, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("ChainId", chain.ChainId.ToString());
            EditorGUILayout.LabelField("Priority", chain.Priority.ToString());
            EditorGUILayout.LabelField("Status", chain.Status.ToString());
            EditorGUILayout.LabelField("Loop", chain.LoopMode.ToString());
            EditorGUILayout.LabelField("LoopDelay", chain.LoopDelaySeconds.ToString("0.###"));
            EditorGUILayout.LabelField("FailedPolicy", chain.FailedPolicy.ToString());
            EditorGUILayout.LabelField("CurrentIndex", chain.CurrentIndex.ToString());

            GUILayout.Space(6f);
            GUILayout.Label("Tasks", EditorStyles.boldLabel);

            if (chain.Tasks == null || chain.Tasks.Count == 0)
            {
                EditorGUILayout.HelpBox("链内无任务。", MessageType.Info);
                return;
            }

            for (var i = 0; i < chain.Tasks.Count; i++)
            {
                var task = chain.Tasks[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    var isCurrent = i == chain.CurrentIndex;
                    var prefix = isCurrent ? ">" : " ";
                    GUILayout.Label($"{prefix}[{i}] P{task.Priority}", GUILayout.Width(80f));
                    var label = string.IsNullOrEmpty(task.Label) ? "-" : task.Label;
                    GUILayout.Label(label, GUILayout.Width(120f));
                    GUILayout.Label(task.TaskType != null ? task.TaskType.Name : "Unknown", GUILayout.Width(180f));
                    GUILayout.Label(task.Status.ToString(), GUILayout.Width(90f));
                    GUILayout.Label(task.BoundUnitId.HasValue ? $"Unit:{task.BoundUnitId.Value}" : "Unit:-", GUILayout.Width(70f));
                    GUILayout.Label(task.LastReason.ToString());
                }
            }
        }

        private void DrawDecisions(UnitTaskSchedulerSnapshot snapshot)
        {
            _showDecisions = EditorGUILayout.Foldout(_showDecisions, "Recent Decisions", true);
            if (!_showDecisions)
            {
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Show", GUILayout.Width(40f));
                _decisionsToShow = Mathf.Clamp(EditorGUILayout.IntField(_decisionsToShow, GUILayout.Width(60f)), 0, 2048);
            }

            var decisions = snapshot.RecentDecisions;
            if (decisions == null || decisions.Count == 0)
            {
                EditorGUILayout.HelpBox("暂无追踪记录（可能未开启 EnableDebugTrace）。", MessageType.Info);
                return;
            }

            var start = Math.Max(0, decisions.Count - _decisionsToShow);
            for (var i = decisions.Count - 1; i >= start; i--)
            {
                var d = decisions[i];
                var taskType = d.TaskType != null ? d.TaskType.Name : "Unknown";
                var unit = d.BoundUnitId.HasValue ? d.BoundUnitId.Value.ToString() : "-";
                EditorGUILayout.LabelField($"{d.Time:0.000}  C{d.ChainId}  T{d.TaskIndex}  {d.Kind}  {taskType}  Unit:{unit}  {d.TaskStatus}  {d.Reason}");
            }
        }

        private static string FormatSourceName(IUnitTaskDebugSnapshotSource source)
        {
            if (source == null)
            {
                return "Null";
            }

            var snapshot = source.Capture();
            return $"{snapshot.Name}@{snapshot.Id}";
        }

        private static UnitTaskSchedulerSnapshot SafeCapture(IUnitTaskDebugSnapshotSource source)
        {
            try
            {
                return source.Capture();
            }
            catch
            {
                return null;
            }
        }
    }
}
