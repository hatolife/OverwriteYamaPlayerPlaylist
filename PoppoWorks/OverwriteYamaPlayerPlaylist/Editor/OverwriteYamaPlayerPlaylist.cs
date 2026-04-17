#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Yamadev.YamaStream.Script;

public sealed class OverwriteYamaPlayerPlaylistWindow : EditorWindow
{
    private const string WindowTitle = "Overwrite YamaPlayer Playlist";
    private const string MenuPath = "Tools/PoppoWorks/Overwrite YamaPlayer Playlist From Json";
    private const string PrefKeyJsonPath = "PoppoWorks.OverwriteYamaPlayerPlaylist.JsonPath";
    private const string PrefKeySearch = "PoppoWorks.OverwriteYamaPlayerPlaylist.Search";
    private const string PrefKeySortKey = "PoppoWorks.OverwriteYamaPlayerPlaylist.SortKey";
    private const string PrefKeySortAscending = "PoppoWorks.OverwriteYamaPlayerPlaylist.SortAscending";
    private const string PrefKeyEnabledIds = "PoppoWorks.OverwriteYamaPlayerPlaylist.EnabledIds";
    private const string PrefKeySummaryFoldout = "PoppoWorks.OverwriteYamaPlayerPlaylist.SummaryFoldout";

    private enum SortKey
    {
        Name,
        Path,
    }

    [Serializable]
    private class ImportedPlaylists
    {
        public List<ImportedPlaylist> playlists;
    }

    [Serializable]
    private class ImportedPlaylist
    {
        public bool Active = true;
        public string Name;
        public List<PlaylistTrack> Tracks = new List<PlaylistTrack>();
        public string YoutubeListId;
    }

    [Serializable]
    private class StringListState
    {
        public List<string> values = new List<string>();
    }

    private sealed class PlayerItem
    {
        public YamaPlayer Player;
        public bool Enabled;
        public string SceneName;
        public string PlayerName;
        public string HierarchyPath;
        public string PersistentId;
    }

    private readonly List<PlayerItem> _players = new List<PlayerItem>();
    private readonly List<string> _lastErrors = new List<string>();
    private Vector2 _scrollPosition;
    private Vector2 _jsonSummaryScrollPosition;
    private string _jsonFilePath = string.Empty;
    private string _searchText = string.Empty;
    private List<ImportedPlaylist> _importedPlaylists;
    private int _selectedInstanceId;
    private SortKey _sortKey = SortKey.Name;
    private bool _sortAscending = true;
    private bool _showJsonSummary = true;

    [MenuItem(MenuPath)]
    public static void OpenWindow()
    {
        OverwriteYamaPlayerPlaylistWindow window = GetWindow<OverwriteYamaPlayerPlaylistWindow>(WindowTitle);
        window.minSize = new Vector2(0f, 420f);
        window.Show();
    }

    private void OnEnable()
    {
        LoadPrefs();
        RefreshPlayers();
        TryReloadJsonSilently();
    }

    private void OnDisable()
    {
        SavePrefs();
    }

    private void OnHierarchyChange()
    {
        RefreshPlayers();
        Repaint();
    }

    private void OnGUI()
    {
        DrawToolbar();
        EditorGUILayout.Space(8f);
        DrawJsonSection();
        EditorGUILayout.Space(8f);
        DrawPlayerListSection();
        EditorGUILayout.Space(8f);
        DrawExecuteSection();
    }

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70f)))
            {
                RefreshPlayers();
            }

            if (GUILayout.Button("All On", EditorStyles.toolbarButton, GUILayout.Width(60f)))
            {
                SetTargets(_players, true);
            }

            if (GUILayout.Button("All Off", EditorStyles.toolbarButton, GUILayout.Width(60f)))
            {
                SetTargets(_players, false);
            }

            if (GUILayout.Button("Filtered On", EditorStyles.toolbarButton, GUILayout.Width(80f)))
            {
                SetTargets(GetSortedFilteredPlayers(), true);
            }

            if (GUILayout.Button("Filtered Off", EditorStyles.toolbarButton, GUILayout.Width(84f)))
            {
                SetTargets(GetSortedFilteredPlayers(), false);
            }

            GUILayout.Space(8f);
            GUIContent searchIcon = EditorGUIUtility.IconContent("Search Icon");
            GUILayout.Label(searchIcon, EditorStyles.label, GUILayout.Width(20f), GUILayout.Height(EditorGUIUtility.singleLineHeight));
            string nextSearchText = GUILayout.TextField(_searchText, GUI.skin.FindStyle("ToolbarSeachTextField") ?? GUI.skin.textField, GUILayout.MinWidth(120f));
            if (nextSearchText != _searchText)
            {
                _searchText = nextSearchText;
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField($"Visible: {GetSortedFilteredPlayers().Count} / Total: {_players.Count}", EditorStyles.miniLabel, GUILayout.Width(150f));
        }
    }

    private void DrawJsonSection()
    {
        EditorGUILayout.LabelField("JSON", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope(GUI.skin.box))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.SelectableLabel(
                    string.IsNullOrEmpty(_jsonFilePath) ? "未選択" : _jsonFilePath,
                    EditorStyles.textField,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight));

                if (GUILayout.Button("Select Json", GUILayout.Width(100f)))
                {
                    SelectJsonFile();
                }

                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_jsonFilePath) || !File.Exists(_jsonFilePath)))
                {
                    if (GUILayout.Button("Reload", GUILayout.Width(70f)))
                    {
                        ReloadJson(showDialogOnError: true);
                    }
                }
            }

            if (_importedPlaylists == null)
            {
                EditorGUILayout.LabelField("JSON 未読込", EditorStyles.miniLabel);
                return;
            }

            int totalTracks = _importedPlaylists.Sum(playlist => playlist?.Tracks?.Count ?? 0);
            EditorGUILayout.LabelField(
                $"Imported playlists: {_importedPlaylists.Count} / Total tracks: {totalTracks}",
                EditorStyles.miniLabel);

            _showJsonSummary = EditorGUILayout.Foldout(_showJsonSummary, "JSON Summary", true);
            if (!_showJsonSummary)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(GUI.skin.box))
            {
                _jsonSummaryScrollPosition = EditorGUILayout.BeginScrollView(
                    _jsonSummaryScrollPosition,
                    GUILayout.MinHeight(80f),
                    GUILayout.MaxHeight(220f));

                foreach (ImportedPlaylist playlist in _importedPlaylists)
                {
                    int trackCount = playlist?.Tracks?.Count ?? 0;
                    string playlistName = string.IsNullOrEmpty(playlist?.Name) ? "(Unnamed Playlist)" : playlist.Name;
                    EditorGUILayout.LabelField($"{playlistName}  ({trackCount} tracks)", EditorStyles.miniLabel);
                }

                EditorGUILayout.EndScrollView();
            }
        }
    }

    private void DrawPlayerListSection()
    {
        EditorGUILayout.LabelField("YamaPlayer List", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope(GUI.skin.box))
        {
            DrawSortControls();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Target", EditorStyles.miniBoldLabel, GUILayout.Width(48f));
                GUILayout.Label("Name", EditorStyles.miniBoldLabel, GUILayout.Width(180f));
                GUILayout.Label("Hierarchy Path", EditorStyles.miniBoldLabel);
            }

            EditorGUILayout.Space(2f);
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, true, true);
            List<PlayerItem> visiblePlayers = GetSortedFilteredPlayers();

            if (_players.Count == 0)
            {
                EditorGUILayout.HelpBox("ロード中のシーンにプレイリスト更新可能な YamaPlayer が見つかりません。", MessageType.Info);
            }
            else if (visiblePlayers.Count == 0)
            {
                EditorGUILayout.HelpBox("検索条件に一致する YamaPlayer がありません。", MessageType.Info);
            }

            foreach (PlayerItem item in visiblePlayers)
            {
                DrawPlayerRow(item);
            }

            EditorGUILayout.EndScrollView();
        }
    }

    private void DrawSortControls()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("Sort", GUILayout.Width(28f));
            _sortKey = (SortKey)EditorGUILayout.EnumPopup(_sortKey, GUILayout.Width(100f));
            _sortAscending = GUILayout.Toggle(_sortAscending, _sortAscending ? "↑" : "↓", "Button", GUILayout.Width(32f));
            GUILayout.FlexibleSpace();
        }
    }

    private void DrawPlayerRow(PlayerItem item)
    {
        Rect rowRect = EditorGUILayout.BeginHorizontal(GUI.skin.box);
        bool isSelected = item.Player != null && item.Player.GetInstanceID() == _selectedInstanceId;
        Color previousColor = GUI.backgroundColor;
        if (isSelected)
        {
            GUI.backgroundColor = new Color(0.6f, 0.8f, 1f);
        }

        item.Enabled = EditorGUILayout.Toggle(item.Enabled, GUILayout.Width(48f));
        GUILayout.Label(item.PlayerName, EditorStyles.label, GUILayout.Width(180f));
        GUILayout.Label(item.HierarchyPath, EditorStyles.label, GUILayout.ExpandWidth(true));

        GUI.backgroundColor = previousColor;
        EditorGUILayout.EndHorizontal();

        if (Event.current.type == EventType.MouseDown && rowRect.Contains(Event.current.mousePosition))
        {
            const float toggleWidth = 48f;
            Rect toggleRect = rowRect;
            toggleRect.width = toggleWidth;
            if (!toggleRect.Contains(Event.current.mousePosition))
            {
                SelectPlayer(item);
                Event.current.Use();
            }
        }
    }

    private void DrawExecuteSection()
    {
        List<PlayerItem> selectedItems = _players.Where(item => item.Enabled).ToList();
        int selectedCount = selectedItems.Count;
        int playlistCount = _importedPlaylists?.Count ?? 0;
        int totalTracks = _importedPlaylists?.Sum(playlist => playlist?.Tracks?.Count ?? 0) ?? 0;

        using (new EditorGUILayout.VerticalScope(GUI.skin.box))
        {
            EditorGUILayout.LabelField(
                $"Selected YamaPlayers: {selectedCount} / Imported playlists: {playlistCount} / Total tracks: {totalTracks}",
                EditorStyles.miniLabel);

            using (new EditorGUI.DisabledScope(_importedPlaylists == null || selectedCount == 0))
            {
                if (GUILayout.Button("Overwrite Selected YamaPlayers", GUILayout.Height(30f)))
                {
                    OverwriteSelectedPlayers(selectedItems);
                }
            }

            if (_lastErrors.Count > 0)
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.HelpBox("前回実行で一部エラーが発生しました。詳細は下の一覧を確認してください。", MessageType.Warning);
                foreach (string error in _lastErrors)
                {
                    EditorGUILayout.SelectableLabel(error, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                }
            }
        }
    }

    private void SelectJsonFile()
    {
        string filePath = EditorUtility.OpenFilePanel("Import playlists", Application.dataPath, "json");
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            return;
        }

        _jsonFilePath = filePath;
        ReloadJson(showDialogOnError: true);
    }

    private bool ReloadJson(bool showDialogOnError)
    {
        _importedPlaylists = null;
        if (string.IsNullOrEmpty(_jsonFilePath) || !File.Exists(_jsonFilePath))
        {
            if (showDialogOnError)
            {
                EditorUtility.DisplayDialog(WindowTitle, "JSON ファイルが見つかりません。", "OK");
            }
            return false;
        }

        try
        {
            string json = File.ReadAllText(_jsonFilePath);
            ImportedPlaylists imported = JsonUtility.FromJson<ImportedPlaylists>(json);
            if (imported == null || imported.playlists == null)
            {
                if (showDialogOnError)
                {
                    EditorUtility.DisplayDialog(WindowTitle, "JSON の playlists を読み取れませんでした。", "OK");
                }
                return false;
            }

            _importedPlaylists = imported.playlists;
            return true;
        }
        catch (Exception ex)
        {
            if (showDialogOnError)
            {
                EditorUtility.DisplayDialog(WindowTitle, $"JSON 読み込み中に例外が発生しました。\n{ex.Message}", "OK");
            }
            return false;
        }
    }

    private void TryReloadJsonSilently()
    {
        if (string.IsNullOrEmpty(_jsonFilePath))
        {
            return;
        }

        ReloadJson(showDialogOnError: false);
    }

    private void RefreshPlayers()
    {
        HashSet<string> enabledIds = LoadEnabledIds();
        bool useSavedIds = enabledIds.Count > 0;
        Dictionary<int, bool> previousStates = _players
            .Where(item => item.Player != null)
            .ToDictionary(item => item.Player.GetInstanceID(), item => item.Enabled);

        _players.Clear();

        foreach (YamaPlayer player in FindSceneYamaPlayers())
        {
            if (player.GetComponentInChildren<PlayListContainer>(true) == null)
            {
                continue;
            }

            string persistentId = GetPersistentId(player);
            int instanceId = player.GetInstanceID();
            bool enabled = previousStates.TryGetValue(instanceId, out bool previousEnabled)
                ? previousEnabled
                : !useSavedIds || enabledIds.Contains(persistentId);

            _players.Add(new PlayerItem
            {
                Player = player,
                Enabled = enabled,
                SceneName = player.gameObject.scene.name,
                PlayerName = player.name,
                HierarchyPath = GetHierarchyPath(player.transform),
                PersistentId = persistentId,
            });
        }

        if (_players.All(item => item.Player == null || item.Player.GetInstanceID() != _selectedInstanceId))
        {
            _selectedInstanceId = 0;
        }
    }

    private void SetTargets(IEnumerable<PlayerItem> items, bool enabled)
    {
        foreach (PlayerItem item in items)
        {
            item.Enabled = enabled;
        }
    }

    private List<PlayerItem> GetSortedFilteredPlayers()
    {
        IEnumerable<PlayerItem> query = _players;

        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            string needle = _searchText.Trim();
            query = query.Where(item =>
                item.SceneName.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 ||
                item.PlayerName.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 ||
                item.HierarchyPath.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        Func<PlayerItem, string> primarySelector = _sortKey switch
        {
            SortKey.Name => item => item.PlayerName,
            _ => item => item.HierarchyPath,
        };

        IOrderedEnumerable<PlayerItem> ordered = _sortAscending
            ? query.OrderBy(primarySelector)
            : query.OrderByDescending(primarySelector);

        return ordered
            .ThenBy(item => item.PlayerName)
            .ThenBy(item => item.HierarchyPath)
            .ToList();
    }

    private void SelectPlayer(PlayerItem item)
    {
        if (item.Player == null)
        {
            return;
        }

        _selectedInstanceId = item.Player.GetInstanceID();
        Selection.activeGameObject = item.Player.gameObject;
        EditorGUIUtility.PingObject(item.Player.gameObject);
        Repaint();
    }

    private void OverwriteSelectedPlayers(List<PlayerItem> selectedItems)
    {
        _lastErrors.Clear();

        int playlistCount = _importedPlaylists.Count;
        int totalTracks = _importedPlaylists.Sum(playlist => playlist?.Tracks?.Count ?? 0);
        string summary = BuildExecutionSummary(selectedItems, playlistCount, totalTracks);
        if (!EditorUtility.DisplayDialog(WindowTitle, summary, "上書きする", "キャンセル"))
        {
            return;
        }

        int updatedCount = 0;
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Overwrite YamaPlayer Playlists");
        int undoGroup = Undo.GetCurrentGroup();

        foreach (PlayerItem item in selectedItems)
        {
            try
            {
                if (item.Player == null)
                {
                    throw new InvalidOperationException("YamaPlayer が見つかりません。");
                }

                PlayListContainer container = item.Player.GetComponentInChildren<PlayListContainer>(true);
                if (container == null)
                {
                    throw new InvalidOperationException("PlayListContainer が見つかりません。");
                }

                OverwriteContainer(container, _importedPlaylists);
                EditorSceneManager.MarkSceneDirty(item.Player.gameObject.scene);
                updatedCount++;
            }
            catch (Exception ex)
            {
                _lastErrors.Add($"{item.SceneName} / {item.HierarchyPath}: {ex.Message}");
            }
        }

        Undo.CollapseUndoOperations(undoGroup);
        AssetDatabase.SaveAssets();

        StringBuilder result = new StringBuilder();
        result.AppendLine("更新完了");
        result.AppendLine();
        result.AppendLine($"Updated: {updatedCount}");
        result.AppendLine($"Failed: {_lastErrors.Count}");
        result.AppendLine($"Imported playlists: {playlistCount}");
        result.AppendLine($"Total tracks: {totalTracks}");

        if (_lastErrors.Count > 0)
        {
            result.AppendLine();
            result.AppendLine("Errors:");
            foreach (string error in _lastErrors)
            {
                result.AppendLine(error);
            }
        }

        EditorUtility.DisplayDialog(WindowTitle, result.ToString(), "OK");
    }

    private static string BuildExecutionSummary(List<PlayerItem> selectedItems, int playlistCount, int totalTracks)
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine($"{selectedItems.Count} 件の YamaPlayer を上書きします。");
        builder.AppendLine($"Playlists: {playlistCount}");
        builder.AppendLine($"Total tracks: {totalTracks}");
        builder.AppendLine();
        builder.AppendLine("対象:");

        foreach (PlayerItem item in selectedItems.Take(10))
        {
            builder.AppendLine($"- {item.SceneName} / {item.HierarchyPath}");
        }

        if (selectedItems.Count > 10)
        {
            builder.AppendLine($"- ... and {selectedItems.Count - 10} more");
        }

        builder.AppendLine();
        builder.Append("既存プレイリストは削除されます。続行しますか？");
        return builder.ToString();
    }

    private static List<YamaPlayer> FindSceneYamaPlayers()
    {
        return Resources
            .FindObjectsOfTypeAll<YamaPlayer>()
            .Where(player =>
                player != null &&
                !EditorUtility.IsPersistent(player) &&
                player.gameObject.scene.IsValid() &&
                player.gameObject.scene.isLoaded)
            .OrderBy(player => player.gameObject.scene.path)
            .ThenBy(player => GetHierarchyPath(player.transform))
            .ToList();
    }

    private static void OverwriteContainer(PlayListContainer container, List<ImportedPlaylist> playlists)
    {
        for (int i = container.transform.childCount - 1; i >= 1; i--)
        {
            Undo.DestroyObjectImmediate(container.transform.GetChild(i).gameObject);
        }

        foreach (ImportedPlaylist playlist in playlists)
        {
            GameObject obj = new GameObject(string.IsNullOrEmpty(playlist.Name) ? "Playlist" : playlist.Name);
            Undo.RegisterCreatedObjectUndo(obj, "Create Playlist");
            obj.SetActive(playlist.Active);
            Undo.SetTransformParent(obj.transform, container.transform, "Parent Playlist");

            SerializedObject serializedObject = new SerializedObject(obj.AddComponent<PlayList>());
            serializedObject.FindProperty("playListName").stringValue = playlist.Name ?? string.Empty;
            serializedObject.FindProperty("YouTubePlayListID").stringValue = playlist.YoutubeListId ?? string.Empty;

            List<PlaylistTrack> tracks = playlist.Tracks ?? new List<PlaylistTrack>();
            SerializedProperty tracksProperty = serializedObject.FindProperty("tracks");
            tracksProperty.arraySize = tracks.Count;
            for (int i = 0; i < tracks.Count; i++)
            {
                PlaylistTrack track = tracks[i] ?? new PlaylistTrack();
                SerializedProperty trackProperty = tracksProperty.GetArrayElementAtIndex(i);
                trackProperty.FindPropertyRelative("Mode").intValue = (int)track.Mode;
                trackProperty.FindPropertyRelative("Title").stringValue = track.Title ?? string.Empty;
                trackProperty.FindPropertyRelative("Url").stringValue = track.Url ?? string.Empty;
            }

            serializedObject.ApplyModifiedProperties();
            GameObjectUtility.EnsureUniqueNameForSibling(obj);
            EditorUtility.SetDirty(obj);
        }

        EditorUtility.SetDirty(container.gameObject);
        PrefabUtility.RecordPrefabInstancePropertyModifications(container);
    }

    private void LoadPrefs()
    {
        _jsonFilePath = EditorPrefs.GetString(PrefKeyJsonPath, string.Empty);
        _searchText = EditorPrefs.GetString(PrefKeySearch, string.Empty);
        _sortKey = (SortKey)EditorPrefs.GetInt(PrefKeySortKey, (int)SortKey.Name);
        _sortAscending = EditorPrefs.GetBool(PrefKeySortAscending, true);
        _showJsonSummary = EditorPrefs.GetBool(PrefKeySummaryFoldout, true);
    }

    private void SavePrefs()
    {
        EditorPrefs.SetString(PrefKeyJsonPath, _jsonFilePath ?? string.Empty);
        EditorPrefs.SetString(PrefKeySearch, _searchText ?? string.Empty);
        EditorPrefs.SetInt(PrefKeySortKey, (int)_sortKey);
        EditorPrefs.SetBool(PrefKeySortAscending, _sortAscending);
        EditorPrefs.SetBool(PrefKeySummaryFoldout, _showJsonSummary);

        StringListState state = new StringListState
        {
            values = _players.Where(item => item.Enabled).Select(item => item.PersistentId).ToList()
        };
        EditorPrefs.SetString(PrefKeyEnabledIds, JsonUtility.ToJson(state));
    }

    private static HashSet<string> LoadEnabledIds()
    {
        string json = EditorPrefs.GetString(PrefKeyEnabledIds, string.Empty);
        if (string.IsNullOrEmpty(json))
        {
            return new HashSet<string>();
        }

        StringListState state = JsonUtility.FromJson<StringListState>(json);
        return state?.values != null
            ? new HashSet<string>(state.values)
            : new HashSet<string>();
    }

    private static string GetPersistentId(YamaPlayer player)
    {
        return GlobalObjectId.GetGlobalObjectIdSlow(player).ToString();
    }

    private static string GetHierarchyPath(Transform transform)
    {
        if (transform == null)
        {
            return string.Empty;
        }

        string path = transform.name;
        while (transform.parent != null)
        {
            transform = transform.parent;
            path = transform.name + "/" + path;
        }

        return path;
    }
}
#endif
