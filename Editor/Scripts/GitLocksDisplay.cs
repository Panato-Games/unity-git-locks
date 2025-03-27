// <copyright file="GitLocksDisplay.cs" company="Tom Duchene and Tactical Adventures">All rights reserved.</copyright>

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
// ReSharper disable All

[InitializeOnLoad]
public class GitLocksDisplay : EditorWindow
{
    private static Dictionary<string, GUIContent> iconsCache;
    private static Dictionary<string, bool> hasLockCache;
    
    private static Texture greenLockIcon;
    private static Texture orangeLockIcon;
    private static Texture redLockIcon; 
    private static Texture mixedLockIcon;

    // Interface sizes
    private static float unlockButtonWidth = 65;
    private static float forceUnlockButtonWidth = 95;
    private static float lockIconWidth = 18;
    private static float scrollbarWidth = 20; // In case the scoll view triggers
    private static float checkboxWidth = 30;
    
    private UnityEngine.Object objectToLock;
    private Vector2 scrollPosMine = Vector2.zero;
    private Vector2 scrollPosOthers = Vector2.zero;

    private static List<GitLocksObject> selectedLocks;

    // Show git history
    private static int showHistoryMaxNumOfFilesBeforeWarning = 5;
    
    private static int currentPageMyLocks = 1;
    private static int currentPageOthersLocks = 1;
    private class GitLockStyles
    {
        public GUIContent lockedByMe;
        public GUIContent lockedBy;
        public GUIContent lockConflictingWith;
        public GUIContent folderContainsMyLocksAndOthers;
        public GUIContent folderContainsMyLocks;
        public GUIContent folderContainsOthersLocks;
        public GUIContent folderContainsConflictingFiles;
    }

    private static GitLockStyles styles;
    private static bool stylesInitialized;
    
    static GitLocksDisplay()
    {
        // Add our own GUI to the project and hierarchy windows
        EditorApplication.projectWindowItemOnGUI += DrawProjectLocks;
        EditorApplication.hierarchyWindowItemOnGUI += DrawHierarchyLocks;

        iconsCache = new Dictionary<string, GUIContent>();
        hasLockCache = new Dictionary<string, bool>();
        
        currentPageMyLocks = 1;
        currentPageOthersLocks = 1;
    }

    private static void CreateStyles()
    {
        styles = new GitLockStyles();
        
        styles.lockedByMe = new GUIContent(GetGreenLockIcon(), "Locked by me");
        styles.lockedBy = new GUIContent(GetOrangeLockIcon(), "Locked by ");
        styles.lockConflictingWith = new GUIContent(GetRedLockIcon(), "Conflicting with lock by");
        styles.folderContainsMyLocksAndOthers = new GUIContent(GetMixedLockIcon(), "Folder contains files locked by me and others");
        styles.folderContainsMyLocks = new GUIContent(GetGreenLockIcon(), "Folder contains files locked by me");
        styles.folderContainsConflictingFiles = new GUIContent(GetRedLockIcon(), "Folder contains conflicting files");
        styles.folderContainsOthersLocks = new GUIContent(GetOrangeLockIcon(), "Folder contains files locked by others");

        stylesInitialized = true;
    }
    
    public static void ForceRefreshIconsCache()
    {
        //Clearing current cache will make the objects to fill it up during first draw
        if(iconsCache != null)
            iconsCache.Clear();
        
        if(hasLockCache != null)
            hasLockCache.Clear();
    }
    
    [MenuItem("Window/Git Locks")]
    public static void ShowWindow()
    {
        // Show existing window instance. If one doesn't exist, make one.
        EditorWindow.GetWindow(typeof(GitLocksDisplay), false, "Git Locks");
    }

    public static void RepaintAll()
    {
        ForceRefreshIconsCache();
        
        EditorApplication.RepaintHierarchyWindow();
        EditorApplication.RepaintProjectWindow();
        
        if (EditorWindow.HasOpenInstances<GitLocksDisplay>())
        {
            EditorWindow locksWindow = GetWindow(typeof(GitLocksDisplay), false, "Git Locks", false);
            locksWindow.Repaint();
        }
    }

    public static void DrawUILine(Color color, int thickness = 2, int padding = 10)
    {
        Rect r = EditorGUILayout.GetControlRect(GUILayout.Height(padding + thickness));
        r.height = thickness;
        r.y += padding / 2;
        r.x -= 2;
        r.width += 6;
        EditorGUI.DrawRect(r, color);
    }

    public static GUIContent GetGUIContentForLockedObject(GitLocksObject lo)
    {
        if (lo == null)
        {
            return null;
        }

        if(!stylesInitialized)
            CreateStyles();
        
        bool isLockConflictingWithUncommitedFile = GitLocks.IsLockedObjectConflictingWithUncommitedFile(lo);

        if (lo.IsMine())
        {
            return styles.lockedByMe;
        }
        else if (isLockConflictingWithUncommitedFile)
        {
            return styles.lockConflictingWith;
        }
        else
        {
            return styles.lockedBy;
        }
    }

    private static void DisplayButton(Rect rect, GUIContent content, string path, GitLocksObject lo)
    {
        if (GUI.Button(rect, content, GUI.skin.label))
        {
            if (lo.IsMine())
            {
                if (!EditorUtility.DisplayDialog("Asset locked by you", "You have locked this asset, you're safe working on it.", "OK", "Unlock"))
                {
                    GitLocks.UnlockFile(lo.path);
                    GitLocks.RefreshLocks();
                }
            }
            else if (GitLocks.IsLockedObjectConflictingWithUncommitedFile(lo))
            {
                EditorUtility.DisplayDialog("Asset locked by someone else and conflicting", "User " + lo.owner.name + " has locked this asset (" + lo.GetLockDateTimeString() + ") and you have uncommited modifications: you should probably discard them as you won't be able to push them.", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Asset locked by someone else", "User " + lo.owner.name + " has locked this asset (" + lo.GetLockDateTimeString() + "), you cannot work on it.", "OK");
            }
        }
    }
    
    public static void DisplayLockIcon(string path, Rect selectionRect, float offset, bool small = false)
    {
        var frame = new Rect(selectionRect);

        // Handle files
        GitLocksObject lo = GitLocks.GetObjectInLockedCache(path);
        
        if (lo != null)
        {
            frame.x += offset + (small ? 3f : 0f);
            frame.width = small ? 12f : 18f;
            
            if (iconsCache != null && iconsCache.ContainsKey(path))
            {
                DisplayButton(frame, iconsCache[path], path, lo);
                return;
            }
            else
            {
                GUIContent lockContent = GetGUIContentForLockedObject(lo);
                string tooltip;

                // Fill tooltip
                if (lo.IsMine())
                {
                    tooltip = "Locked by me";
                }
                else if (GitLocks.IsLockedObjectConflictingWithUncommitedFile(lo))
                {
                    tooltip = "Conflicting with lock by " + lo.owner.name;
                }
                else
                {
                    tooltip = "Locked by " + lo.owner.name;
                }

                lockContent.tooltip = tooltip;

                if(iconsCache != null)
                    iconsCache.TryAdd(path, lockContent);

                if (hasLockCache != null)
                    hasLockCache.TryAdd(path, true);

                DisplayButton(frame, lockContent, path, lo);
                return;
            }
        }
        
        // Handle folders
        if (Directory.Exists(path) && GitLocks.LockedObjectsCache != null)
        {
            frame.x += offset + 15;
            frame.width = 15f;
            
            if (iconsCache != null && iconsCache.ContainsKey(path))
            {
                GUI.Button(frame, iconsCache[path], GUI.skin.label);
                return;
            }
            else
            {
                bool containsOneOfMyLocks = false;
                bool containsOneOfOtherLocks = false;
                bool containsOneConflictingLock = false;

                for (var i = 0; i < GitLocks.LockedObjectsCache.Count; i++)
                {
                    var locksObject = GitLocks.LockedObjectsCache[i];

                    string folderPath = path + "/";
                    if (locksObject.path.Contains(folderPath))
                    {
                        if (locksObject.IsMine())
                        {
                            containsOneOfMyLocks = true;
                        }
                        else if (GitLocks.IsLockedObjectConflictingWithUncommitedFile(locksObject))
                        {
                            containsOneConflictingLock = true;
                            containsOneOfOtherLocks = true;
                        }
                        else
                        {
                            containsOneOfOtherLocks = true;
                        }

                        if (containsOneOfMyLocks && containsOneOfOtherLocks)
                        {
                            break;
                        }
                    }
                }

                if (containsOneOfMyLocks || containsOneOfOtherLocks)
                {
                    GUIContent guiContent;

                    if (!stylesInitialized)
                        CreateStyles();

                    if (containsOneOfMyLocks && containsOneOfOtherLocks)
                    {
                        guiContent = styles.folderContainsMyLocksAndOthers;
                    }
                    else if (containsOneOfMyLocks)
                    {
                        guiContent = styles.folderContainsMyLocks;
                    }
                    else if (containsOneConflictingLock)
                    {
                        guiContent = styles.folderContainsConflictingFiles;
                    }
                    else
                    {
                        guiContent = styles.folderContainsOthersLocks;
                    }

                    
                    if(iconsCache != null)
                        iconsCache.Add(path, guiContent);
                    
                    if (hasLockCache != null)
                        hasLockCache.TryAdd(path, true);
                    
                    GUI.Button(frame, guiContent, GUI.skin.label);
                    return;
                }
            }
        }
        
        if (hasLockCache != null && !string.IsNullOrEmpty(path))
            hasLockCache.TryAdd(path, false);
    }

    // -----------------------
    // Project window features
    // -----------------------
    [MenuItem("Assets/Git LFS Lock %#l", false, 1100)]
    private static void ItemMenuLock()
    {
        UnityEngine.Object[] selected = Selection.GetFiltered<UnityEngine.Object>(SelectionMode.DeepAssets);
        List<string> paths = new List<string>();
        foreach (UnityEngine.Object o in selected)
        {
            string path = AssetDatabase.GetAssetPath(o.GetInstanceID());
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                continue; // Folders are not lockable, skip this asset
            }

            paths.Add(path);
        }

        GitLocks.LockFiles(paths);
        GitLocks.RefreshLocks();
    }

    [MenuItem("Assets/Git LFS Lock %#l", true)]
    private static bool ValidateItemMenuLock()
    {
        if (!GitLocks.IsEnabled())
        {
            return false; // Early return if the whole tool is disabled
        }

        // Don't allow if the locked cache hasn't been built
        if (GitLocks.LastRefresh <= DateTime.MinValue)
        {
            return false;
        }

        UnityEngine.Object[] selected = Selection.GetFiltered<UnityEngine.Object>(SelectionMode.Assets);
        foreach (UnityEngine.Object o in selected)
        {
            string path = AssetDatabase.GetAssetPath(o.GetInstanceID());
            if (Directory.Exists(path))
            {
                foreach (GitLocksObject lo in GitLocks.LockedObjectsCache)
                {
                    string folderPath = path + "/";
                    if (lo.path.Contains(folderPath))
                    {
                        return false;
                    }
                }
            }
            else if (!GitLocks.IsObjectAvailableToLock(path))
            {
                return false;
            }
        }

        return true;
    }

    [MenuItem("Assets/Git LFS Unlock %#u", false, 1101)]
    private static void ItemMenuUnlock()
    {
        List<string> paths = new List<string>();
        UnityEngine.Object[] selected = Selection.GetFiltered<UnityEngine.Object>(SelectionMode.Assets);
        foreach (UnityEngine.Object o in selected)
        {
            string path = AssetDatabase.GetAssetPath(o.GetInstanceID());
            if (Directory.Exists(path))
            {
                foreach (GitLocksObject lo in GitLocks.LockedObjectsCache)
                {
                    string folderPath = path + "/";
                    if (lo.path.Contains(folderPath))
                    {
                        if (GitLocks.IsObjectAvailableToUnlock(lo))
                        {
                            paths.Add(lo.path);
                        }
                    }
                }
            }
            else if (GitLocks.IsObjectAvailableToUnlock(path))
            {
                paths.Add(path);
            }
        }

        GitLocks.UnlockFiles(paths);
        GitLocks.RefreshLocks();
    }

    [MenuItem("Assets/Git LFS Unlock %#u", true)]
    private static bool ValidateItemMenuUnlock()
    {
        if (!GitLocks.IsEnabled())
        {
            return false; // Early return if the whole tool is disabled
        }

        // Don't allow if the locked cache hasn't been built
        if (GitLocks.LastRefresh <= DateTime.MinValue)
        {
            return false;
        }

        UnityEngine.Object[] selected = Selection.GetFiltered<UnityEngine.Object>(SelectionMode.Assets);
        bool foundObjectToUnlock = false;
        foreach (UnityEngine.Object o in selected)
        {
            string path = AssetDatabase.GetAssetPath(o.GetInstanceID());
            if (Directory.Exists(path))
            {
                foreach (GitLocksObject lo in GitLocks.LockedObjectsCache)
                {
                    string folderPath = path + "/";
                    if (lo.path.Contains(folderPath))
                    {
                        if (lo.IsMine())
                        {
                            foundObjectToUnlock = true;
                        }
                        else
                        {
                            return false;
                        }
                    }
                }
            }
            else if (GitLocks.IsObjectAvailableToUnlock(path))
            {
                foundObjectToUnlock = true;
            }
        }

        return foundObjectToUnlock;
    }

    // -------------------------
    // Hierarchy window features
    // -------------------------
    [MenuItem("GameObject/Git LFS Lock", false, 40)]
    private static void ItemMenuLockHierarchy()
    {
        List<string> paths = new List<string>();
        foreach (UnityEngine.Object o in Selection.objects)
        {
            string path = GitLocks.GetAssetPathFromPrefabGameObject(o.GetInstanceID());
            paths.Add(path);
        }

        GitLocks.LockFiles(paths);

        // Clear the selection to make sure it's called only once
        Selection.objects = null;

        GitLocks.RefreshLocks();
    }

    [MenuItem("GameObject/Git LFS Lock", true)]
    private static bool ValidateItemMenuLockHierarchy()
    {
        if (!GitLocks.IsEnabled())
        {
            return false; // Early return if the whole tool is disabled
        }

        // Don't allow if the locked cache hasn't been built
        if (GitLocks.LastRefresh <= DateTime.MinValue)
        {
            return false;
        }

        foreach (UnityEngine.Object o in Selection.objects)
        {
            if (o == null)
            {
                return false;
            }

            string path = GitLocks.GetAssetPathFromPrefabGameObject(o.GetInstanceID());
            if (path == null || path == string.Empty || !GitLocks.IsObjectAvailableToLock(path))
            {
                return false;
            }
        }

        return true;
    }

    [MenuItem("GameObject/Git LFS Unlock", false, 41)]
    private static void ItemMenuUnlockHierarchy()
    {
        List<string> paths = new List<string>();
        foreach (UnityEngine.Object o in Selection.objects)
        {
            string path = GitLocks.GetAssetPathFromPrefabGameObject(o.GetInstanceID());
            paths.Add(path);
        }

        GitLocks.UnlockFiles(paths);

        // Clear the selection to make sure it's called only once
        Selection.objects = null;

        GitLocks.RefreshLocks();
    }

    [MenuItem("GameObject/Git LFS Unlock", true)]
    private static bool ValidateItemMenuUnlockHierarchy()
    {
        if (!GitLocks.IsEnabled())
        {
            return false; // Early return if the whole tool is disabled
        }

        // Don't allow if the locked cache hasn't been built
        if (GitLocks.LastRefresh <= DateTime.MinValue)
        {
            return false;
        }

        foreach (UnityEngine.Object o in Selection.objects)
        {
            if (o == null)
            {
                return false;
            }

            string path = GitLocks.GetAssetPathFromPrefabGameObject(o.GetInstanceID());
            if (path == null || path == string.Empty || !GitLocks.IsObjectAvailableToUnlock(path))
            {
                return false;
            }
        }

        return true;
    }

    // -------------------------------------------
    // Draw icons in hierarchy and project windows
    // -------------------------------------------
    private static void DrawProjectLocks(string guid, Rect selectionRect)
    {
        if (!GitLocks.IsEnabled())
        {
            return; // Early return if the whole tool is disabled
        }

        GitLocks.CheckLocksRefresh();

        string path = AssetDatabase.GUIDToAssetPath(guid);

        if(string.IsNullOrEmpty(path))
            return;
        
        if (hasLockCache.ContainsKey(path))
        {
            //Second display, only re-draw if there was a lock
            if(hasLockCache[path])
                DisplayLockIcon(path, selectionRect, -12f);
        }
        else
        {
            //First display, check if there is a lock
            DisplayLockIcon(path, selectionRect, -12f);
        }
    }

    private static void DrawHierarchyLocks(int instanceID, Rect selectionRect)
    {
        if (!GitLocks.IsEnabled())
        {
            return; // Early return if the whole tool is disabled
        }

        GitLocks.CheckLocksRefresh();

        string path = string.Empty;
        bool small = false;

        // Handle scenes
        path = GitLocks.GetSceneFromInstanceID(instanceID).path;

        // Handle prefabs
        string tmpPath = GitLocks.GetAssetPathFromPrefabGameObject(instanceID);
        if (tmpPath != string.Empty)
        {
            path = tmpPath;
            small = !GitLocks.IsObjectPrefabRoot(instanceID);
        }

        // Display
        if (path != string.Empty)
        {
            DisplayLockIcon(path, selectionRect, -30f, small);
        }
    }

    // ------------
    // File history
    // ------------
    [MenuItem("Assets/Show Git History", false, 1101)]
    private static void ItemMenuGitHistory()
    {
        UnityEngine.Object[] selected = Selection.GetFiltered<UnityEngine.Object>(SelectionMode.Assets);
        
        // Display a warning if you're about to open many CLIs or browser tabs to prevent slowing down your computer if you misclick
        if (selected.Length <= showHistoryMaxNumOfFilesBeforeWarning || EditorUtility.DisplayDialog("Are you sure?", "More than " + showHistoryMaxNumOfFilesBeforeWarning + " files have been selected, are you sure you want to open the history for all of them?", "Yes", "Cancel"))
        {
            foreach (UnityEngine.Object o in selected)
            {
                string path = AssetDatabase.GetAssetPath(o.GetInstanceID());
                if (Directory.Exists(path))
                {
                    continue; // Folders are not lockable, skip this asset
                }

                if (EditorPrefs.GetBool("gitLocksShowHistoryInBrowser", false))
                {
                    string url = EditorPrefs.GetString("gitLocksShowHistoryInBrowserUrl");
                    if (url != string.Empty && url.Contains("$branch") && url.Contains("$assetPath"))
                    {
                        url = url.Replace("$branch", GitLocks.GetCurrentBranch());
                        url = url.Replace("$assetPath", path);
                        UnityEngine.Application.OpenURL(url);
                    }
                    else
                    {
                        UnityEngine.Debug.LogError("URL was not formatted correctly to show the file's history in your browser: it must be formatted like https://github.com/MyUserName/MyRepo/blob/$branch/$assetPath");
                    }
                }
                else
                {
                    GitLocks.ExecuteProcessTerminal("git", "log \"" + path + "\"", true);
                }
            }
        }
    }

    [MenuItem("Assets/Show Git History", true)]
    private static bool ValidateItemMenuGitHistory()
    {
        UnityEngine.Object[] selected = Selection.GetFiltered<UnityEngine.Object>(SelectionMode.Assets);
        foreach (UnityEngine.Object o in selected)
        {
            string path = AssetDatabase.GetAssetPath(o.GetInstanceID());
            if (Directory.Exists(path))
            {
                return false;
            }
        }

        return true;
    }

    // -----------
    // Toolbox
    // -----------
    private static void DrawLockedObjectLine(GitLocksObject lo, bool myLock = false)
    {
        UnityEngine.Object lockedObj = lo.GetObjectReference();

        var ogColor = GUI.color;

        if (lockedObj == null)
            GUI.color = Color.yellow;
        
        float totalOtherWidth = EditorGUIUtility.currentViewWidth - lockIconWidth - scrollbarWidth - checkboxWidth;
        totalOtherWidth -= EditorPrefs.GetBool("gitLocksShowForceButtons") ? forceUnlockButtonWidth : 0;
        totalOtherWidth -= myLock ? unlockButtonWidth : 0;
        float othersWidth = totalOtherWidth / 3;

        GUILayout.BeginHorizontal();

        GUIContent guiContent = GetGUIContentForLockedObject(lo);

        // Checkboxes for custom selection
        if (myLock)
        {
            if (selectedLocks == null)
            {
                selectedLocks = new List<GitLocksObject>();
            }

            bool checkbox = GUILayout.Toggle(selectedLocks.Contains(lo), "");
            if (checkbox && !selectedLocks.Contains(lo))
            {
                selectedLocks.Add(lo);
            }
            else if (!checkbox && selectedLocks.Contains(lo))
            {
                selectedLocks.Remove(lo);
            }
        }

        GUILayout.Button(guiContent.image, GUI.skin.label, GUILayout.Height(18), GUILayout.Width(18));

        EditorGUI.BeginDisabledGroup(true);

        if (lockedObj != null)
        {
            EditorGUILayout.ObjectField(lockedObj, lockedObj.GetType(), false, GUILayout.Width(othersWidth));
        }
        else
        {
            EditorGUILayout.TextField("Asset not found at path! Unlock it if not needed and/or lock it again.", GUILayout.Width(othersWidth));
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.Label(new GUIContent(lo.path, lo.path), GUILayout.Width(othersWidth));

        GUILayout.Label(new GUIContent(lo.owner.name, lo.GetLockDateTimeString()), GUILayout.Width(othersWidth));

        if (myLock)
        {
            if (GUILayout.Button("Unlock"))
            {
                GitLocks.UnlockFile(lo.path);
                GitLocks.RefreshLocks();
            }
        }

        if (EditorPrefs.GetBool("gitLocksShowForceButtons"))
        {
            if (GUILayout.Button("Force unlock"))
            {
                if (EditorUtility.DisplayDialog("Force unlock ?", "Are you sure you want to force the unlock ? It may mess with a teammate's work !", "Yes, I know the risks", "Cancel, I'm not sure"))
                {
                    GitLocks.UnlockFile(lo.path, true);
                    GitLocks.RefreshLocks();
                }
            }
        }

        GUILayout.EndHorizontal();

        GUI.color = ogColor;
    }

    private static Texture GetGreenLockIcon(bool forceReload = false)
    {
        if (greenLockIcon == null || forceReload)
        {
            if (EditorPrefs.HasKey("gitLocksColorblindMode") && EditorPrefs.GetBool("gitLocksColorblindMode"))
            {
                greenLockIcon = (Texture)AssetDatabase.LoadAssetAtPath("Packages/com.tomduchene.unity-git-locks/Editor/Textures/greenLock_cb.png", typeof(Texture));
            }
            else
            {
                greenLockIcon = (Texture)AssetDatabase.LoadAssetAtPath("Packages/com.tomduchene.unity-git-locks/Editor/Textures/greenLock.png", typeof(Texture));
            }
        }

        return greenLockIcon;
    }

    private static Texture GetOrangeLockIcon(bool forceReload = false)
    {
        if (orangeLockIcon == null || forceReload)
        {
            if (EditorPrefs.HasKey("gitLocksColorblindMode") && EditorPrefs.GetBool("gitLocksColorblindMode"))
            {
                orangeLockIcon = (Texture)AssetDatabase.LoadAssetAtPath("Packages/com.tomduchene.unity-git-locks/Editor/Textures/orangeLock_cb.png", typeof(Texture));
            }
            else
            {
                orangeLockIcon = (Texture)AssetDatabase.LoadAssetAtPath("Packages/com.tomduchene.unity-git-locks/Editor/Textures/orangeLock.png", typeof(Texture));
            }
        }

        return orangeLockIcon;
    }

    private static Texture GetRedLockIcon(bool forceReload = false)
    {
        if (redLockIcon == null || forceReload)
        {
            if (EditorPrefs.HasKey("gitLocksColorblindMode") && EditorPrefs.GetBool("gitLocksColorblindMode"))
            {
                redLockIcon = (Texture)AssetDatabase.LoadAssetAtPath("Packages/com.tomduchene.unity-git-locks/Editor/Textures/redLock_cb.png", typeof(Texture));
            }
            else
            {
                redLockIcon = (Texture)AssetDatabase.LoadAssetAtPath("Packages/com.tomduchene.unity-git-locks/Editor/Textures/redLock.png", typeof(Texture));
            }
        }

        return redLockIcon;
    }

    private static Texture GetMixedLockIcon(bool forceReload = false)
    {
        if (mixedLockIcon == null || forceReload)
        {
            if (EditorPrefs.HasKey("gitLocksColorblindMode") && EditorPrefs.GetBool("gitLocksColorblindMode"))
            {
                mixedLockIcon = (Texture)AssetDatabase.LoadAssetAtPath("Packages/com.tomduchene.unity-git-locks/Editor/Textures/mixedLock_cb.png", typeof(Texture));
            }
            else
            {
                mixedLockIcon = (Texture)AssetDatabase.LoadAssetAtPath("Packages/com.tomduchene.unity-git-locks/Editor/Textures/mixedLock.png", typeof(Texture));
            }
        }

        return mixedLockIcon;
    }

    public static void RefreshLockIcons()
    {
        GetGreenLockIcon(true);
        GetOrangeLockIcon(true);
        GetRedLockIcon(true);
        GetMixedLockIcon(true);
    }

    // ------------------------
    // Git lock window features
    // ------------------------
    private void OnGUI()
    {
        if (!GitLocks.IsEnabled())
        {
            GUILayout.Label("Tool disabled", EditorStyles.wordWrappedLabel);
            if (GUILayout.Button("Enable in preferences"))
            {
                SettingsService.OpenUserPreferences("Preferences/Git Locks");
            }
        }
        else
        {
            GitLocks.CheckLocksRefresh();

            EditorGUILayout.Space(10);

            if (GitLocks.GetGitUsername() == string.Empty)
            {
                // If username hasn't been set, show this window instead of the main one to ask the user to input it's username
                GUILayout.Label("You need to setup your Git LFS host username for the tool to work properly, most likely your Github username", EditorStyles.wordWrappedLabel);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Go on github"))
                {
                    UnityEngine.Application.OpenURL("https://github.com/");
                }

                if (GUILayout.Button("Setup username in preferences"))
                {
                    SettingsService.OpenUserPreferences("Preferences/Git Locks");
                }

                GUILayout.EndHorizontal();
            }
            else
            {
                // Refresh locks info
                GUILayout.BeginHorizontal();
                if (GitLocks.CurrentlyRefreshing)
                {
                    GUILayout.Label("Last refresh time : currently refreshing...");
                }
                else
                {
                    GUILayout.Label("Last refresh time : " + GitLocks.LastRefresh.ToShortTimeString());
                }

                EditorGUI.BeginDisabledGroup(true);
                bool autoRefresh = EditorPrefs.GetBool("gitLocksAutoRefreshLocks", true);
                if (autoRefresh)
                {
                    int refreshLocksInterval = EditorPrefs.GetInt("gitLocksRefreshLocksInterval");
                    string refreshIntervalStr = "Auto refresh every " + refreshLocksInterval.ToString() + " " + (refreshLocksInterval > 1 ? "minutes" : "minute");
                    GUILayout.Label(refreshIntervalStr);
                }
                else
                {
                    GUILayout.Label("Manual refresh only");
                }

                EditorGUI.EndDisabledGroup();
                if (GUILayout.Button("Setup"))
                {
                    SettingsService.OpenUserPreferences("Preferences/Git Locks");
                }

                GUILayout.EndHorizontal();

                // Refresh button
                if (GUILayout.Button("Refresh Git LFS locks"))
                {
                    GitLocks.RefreshLocks();
                }

                DrawUILine(Color.grey, 2, 20);

                // Lock action
                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Lock an asset :");
                this.objectToLock = EditorGUILayout.ObjectField(this.objectToLock, typeof(UnityEngine.Object), true);
                if (GUILayout.Button("Lock"))
                {
                    string path = AssetDatabase.GetAssetPath(this.objectToLock);

                    if (path == null || path == string.Empty)
                    {
                        path = GitLocks.GetAssetPathFromPrefabGameObject(this.objectToLock.GetInstanceID());
                    }

                    GitLocks.LockFile(path);
                    this.objectToLock = null;
                    GitLocks.RefreshLocks();
                }

                GUILayout.EndHorizontal();

                GUILayout.Space(5);

                
                // My locks
                GUILayout.BeginVertical();
                GUILayout.BeginHorizontal();
                
                EditorGUILayout.LabelField("My locks", EditorStyles.boldLabel);
                
                GUILayout.FlexibleSpace();

                bool allSelected = false;
                if (selectedLocks != null && GitLocks.GetMyLocks() != null && selectedLocks.Count > 0)
                {
                    allSelected = selectedLocks.Count == GitLocks.GetMyLocks().Count;
                }
                bool allSelectedCheckbox = GUILayout.Toggle(allSelected, "All");
                if (selectedLocks != null && allSelectedCheckbox && !allSelected)
                {
                    selectedLocks.Clear();
                    selectedLocks.AddRange(GitLocks.GetMyLocks());
                }
                else if (selectedLocks != null && !allSelectedCheckbox && allSelected)
                {
                    selectedLocks.Clear();
                }

                GUILayout.Space(10);

                if (GUILayout.Button("Unlock selected", GUILayout.Width(120)))
                {
                    GitLocks.UnlockMultipleLocks(selectedLocks);
                    selectedLocks.Clear();
                }

                var maxLocksPerPage = EditorPrefs.GetInt("maxLocksPerPage", 20);
                GUILayout.EndHorizontal();
                
                var myLocks = GitLocks.GetMyLocks();

                DrawPagination(ref currentPageMyLocks, myLocks.Count);
                
                EditorGUILayout.EndVertical();

                if (myLocks != null && GitLocks.LockedObjectsCache != null && GitLocks.LockedObjectsCache.Count > 0)
                {
                    // Compute min height for "My locks"
                    int numOfLines = Mathf.Min(EditorPrefs.GetInt("numOfMyLocksDisplayed", 5), GitLocks.GetMyLocks().Count);
                    float scrollViewHeight = ((float)numOfLines + 0.5f) * 21.0f; // Line height hardcoded for now, +0.5 is used to make sure there's something crossing the float line for clarity

                    this.scrollPosMine = EditorGUILayout.BeginScrollView(this.scrollPosMine, GUILayout.MinHeight(scrollViewHeight));

                    var pageMinIndex = (currentPageMyLocks - 1) * maxLocksPerPage;
                    var pageMaxIndex = currentPageMyLocks * maxLocksPerPage;
                    
                    for (var i = pageMinIndex; i < pageMaxIndex && i < myLocks.Count; i++)
                    {
                        var lo = myLocks[i];

                        DrawLockedObjectLine(lo, true);
                    }

                    EditorGUILayout.EndScrollView();
                }

                DrawUILine(Color.grey, 2, 20);

                // Other locks
                GUILayout.BeginVertical();
                EditorGUILayout.LabelField("Other locks", EditorStyles.boldLabel);
                
                var otherLocks = GitLocks.GetOtherLocks();

                DrawPagination(ref currentPageOthersLocks, otherLocks.Count);
               
                GUILayout.EndVertical();
                
                if (GitLocks.LockedObjectsCache != null && GitLocks.LockedObjectsCache.Count > 0)
                {
                    this.scrollPosOthers = EditorGUILayout.BeginScrollView(this.scrollPosOthers);
                    
                    var pageMinIndex = (currentPageOthersLocks - 1) * maxLocksPerPage;
                    var pageMaxIndex = currentPageOthersLocks * maxLocksPerPage;
                    
                    for (var i = pageMinIndex; i < pageMaxIndex && i < otherLocks.Count; i++)
                    {
                        var lo = otherLocks[i];

                        DrawLockedObjectLine(lo);
                    }

                    EditorGUILayout.EndScrollView();

                }
            }
        }
    }

    private void DrawPagination(ref int currentPage, int locksAmount)
    {
        GUILayout.BeginHorizontal();

        EditorGUILayout.LabelField("Page");
        currentPage = EditorGUILayout.IntField(currentPage, GUILayout.Width(50));

        if (currentPage < 1)
            currentPage = 1;

        var maxLocksPerPage = EditorPrefs.GetInt("maxLocksPerPage", 20);
        var maxPage = (locksAmount / maxLocksPerPage);

        //More on the last page
        if (locksAmount % maxLocksPerPage != 0)
            maxPage++;

        if (maxPage < 1)
            maxPage = 1;

        if (currentPage > maxPage)
            currentPage = maxPage;

        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.IntField(maxPage, GUILayout.Width(50));
        EditorGUI.EndDisabledGroup();

        EditorGUI.BeginDisabledGroup(currentPage <= 1);

        if (GUILayout.Button("<<", GUILayout.Width(30)))
        {
            currentPage--;

            if (currentPage < 1)
                currentPage = 1;
        }

        EditorGUI.EndDisabledGroup();

        EditorGUI.BeginDisabledGroup(currentPage >= maxPage);

        if (GUILayout.Button(">>", GUILayout.Width(30)))
        {
            currentPage++;

            if (currentPage > maxPage)
                currentPage = maxPage;
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.EndHorizontal();
    }

}
