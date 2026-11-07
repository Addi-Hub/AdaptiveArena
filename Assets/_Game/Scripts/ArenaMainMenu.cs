using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public sealed class ArenaMainMenu : MonoBehaviour
{
    private enum Page { Home, Bots, Options }

    public const string SceneName = "MainMenu";
    private const string VolumeKey = "Arena.MasterVolume";
    private static readonly Color Background = new Color(0.035f, 0.055f, 0.085f);
    private static readonly Color Accent = new Color(0.30f, 0.88f, 0.76f);
    private static readonly Color Muted = new Color(0.72f, 0.78f, 0.84f);

    public static bool UseBaselineBot { get; private set; }

    private Page page;
    private GUIStyle heading;
    private GUIStyle copy;
    private GUIStyle button;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSelection() => UseBaselineBot = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void LoadVolume() => AudioListener.volume = PlayerPrefs.GetFloat(VolumeKey, 1f);

    private void Update()
    {
        if (Keyboard.current?.escapeKey.wasPressedThisFrame == true)
            page = Page.Home;
    }

    private void OnGUI()
    {
        heading ??= TextStyle(49, FontStyle.Bold, Color.white);
        copy ??= TextStyle(19, FontStyle.Normal, Muted);
        button ??= new GUIStyle(GUI.skin.button) { fontSize = 20, fontStyle = FontStyle.Bold };

        Paint(new Rect(0, 0, Screen.width, Screen.height), Background);
        float scale = Mathf.Min(Screen.width / 900f, Screen.height / 550f);
        if (scale <= 0f)
            return;

        Matrix4x4 previous = GUI.matrix;
        float left = (Screen.width - 900f * scale) / 2f;
        float top = (Screen.height - 550f * scale) / 2f;
        GUI.matrix = Matrix4x4.TRS(new Vector3(left, top, 0), Quaternion.identity,
            new Vector3(scale, scale, 1)) * previous;

        Paint(new Rect(265, 61, 370, 4), Accent);
        switch (page)
        {
            case Page.Home: DrawHome(); break;
            case Page.Bots: DrawBots(); break;
            case Page.Options: DrawOptions(); break;
        }

        if (page != Page.Home && Button(477, "Back"))
            page = Page.Home;
        GUI.matrix = previous;
    }

    private void DrawHome()
    {
        Title("ADAPTIVE ARENA", "DUEL  /  TRAIN  /  IMPROVE");
        if (Button(209, "Play vs Bot")) page = Page.Bots;
#if UNITY_EDITOR
        if (Button(269, "Training")) LaunchTrainingDashboard();
#else
        using (new GUI.DisabledScope(true))
            Button(269, "Training (Unity Editor)");
#endif
        if (Button(329, "Options")) page = Page.Options;
        if (Button(389, "Quit"))
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }

    private void DrawBots()
    {
        Title("CHOOSE A BOT", "SCRIPTED OPPONENTS");
        if (Button(209, "Sparring Partner"))
            StartDuel(false);
        GUI.Label(new Rect(220, 263, 460, 32), "Practices heavy swings with long pauses.", copy);

        if (Button(314, "Baseline Bot"))
            StartDuel(true);
        GUI.Label(new Rect(200, 368, 500, 32), "Mixes light and heavy swings, dodges, and heals.", copy);
    }

#if UNITY_EDITOR
    [UnityEditor.MenuItem("Adaptive Arena/Training Dashboard")]
    private static void LaunchTrainingDashboard()
    {
        const string interpreterKey = "AdaptiveArena.TrainingPython";
        string root = System.IO.Path.GetDirectoryName(Application.dataPath);
        string defaultPython = System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),
            "anaconda3", "envs", "adaptive-arena", "python.exe");
        string python = UnityEditor.EditorPrefs.GetString(interpreterKey, defaultPython);
        if (!System.IO.File.Exists(python))
        {
            python = UnityEditor.EditorUtility.OpenFilePanel(
                "Select Python from your ML-Agents environment", "", "exe");
            if (string.IsNullOrEmpty(python))
                return;
            UnityEditor.EditorPrefs.SetString(interpreterKey, python);
        }

        string script = System.IO.Path.Combine(root, "training", "dashboard.py");
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = python,
                Arguments = $"\"{script}\"",
                WorkingDirectory = root,
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch (System.ComponentModel.Win32Exception error)
        {
            UnityEditor.EditorUtility.DisplayDialog("Training Dashboard", error.Message, "OK");
        }
    }
#endif

    private void DrawOptions()
    {
        Title("OPTIONS", "AUDIO");
        GUI.Label(new Rect(265, 219, 370, 32),
            $"Master volume   {Mathf.RoundToInt(AudioListener.volume * 100f)}%", copy);
        float volume = GUI.HorizontalSlider(new Rect(265, 276, 370, 28), AudioListener.volume, 0f, 1f);
        if (!Mathf.Approximately(volume, AudioListener.volume))
        {
            AudioListener.volume = volume;
            PlayerPrefs.SetFloat(VolumeKey, volume);
        }
    }

    private void Title(string title, string subtitle)
    {
        GUI.Label(new Rect(90, 79, 720, 65), title, heading);
        GUI.Label(new Rect(90, 148, 720, 28), subtitle, copy);
    }

    private bool Button(float top, string label) => GUI.Button(new Rect(265, top, 370, 48), label, button);

    private static void StartDuel(bool baselineBot)
    {
        UseBaselineBot = baselineBot;
        SceneManager.LoadScene("Arena");
    }

    private static GUIStyle TextStyle(int size, FontStyle weight, Color color)
    {
        var style = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = size,
            fontStyle = weight
        };
        style.normal.textColor = color;
        return style;
    }

    private static void Paint(Rect rect, Color color)
    {
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
    }
}


#if UNITY_EDITOR
[UnityEditor.InitializeOnLoad]
internal static class ArenaTrainingPlayMode
{
    private const string TrainingScene = "Assets/_Game/Scenes/SelfPlayArena.unity";
    private const string OverrideActive = "AdaptiveArena.TrainingOverrideActive";
    private const string PreviousScene = "AdaptiveArena.PreviousPlayScene";
    private static readonly string Request = System.IO.Path.Combine(
        System.IO.Path.GetDirectoryName(Application.dataPath), "Library", "ArenaTraining", "play-request");
    private static double nextPoll;

    static ArenaTrainingPlayMode()
    {
        UnityEditor.EditorApplication.update += Poll;
        UnityEditor.EditorApplication.playModeStateChanged += RestoreStartScene;
    }

    private static void Poll()
    {
        double now = UnityEditor.EditorApplication.timeSinceStartup;
        if (now < nextPoll)
            return;
        nextPoll = now + 0.25;
        if (UnityEditor.EditorApplication.isCompiling || UnityEditor.EditorApplication.isUpdating ||
            !System.IO.File.Exists(Request))
            return;

        try
        {
            if (System.DateTime.UtcNow - System.IO.File.GetLastWriteTimeUtc(Request) > System.TimeSpan.FromSeconds(60))
            {
                System.IO.File.Delete(Request);
                return;
            }

            if (UnityEditor.EditorApplication.isPlaying)
            {
                string scene = SceneManager.GetActiveScene().name;
                if (scene == ArenaMainMenu.SceneName || scene == "SelfPlayArena")
                    UnityEditor.EditorApplication.isPlaying = false;
                return;
            }

            if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            var sceneAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.SceneAsset>(TrainingScene);
            System.IO.File.Delete(Request);
            if (sceneAsset == null)
            {
                Debug.LogError($"Adaptive Arena could not find {TrainingScene}.");
                return;
            }

            var previous = UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene;
            UnityEditor.SessionState.SetString(PreviousScene, UnityEditor.AssetDatabase.GetAssetPath(previous) ?? "");
            UnityEditor.SessionState.SetBool(OverrideActive, true);
            UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene = sceneAsset;
            UnityEditor.EditorApplication.EnterPlaymode();
        }
        catch (System.IO.IOException)
        {
            // The dashboard may have removed the request after its trainer exited.
        }
    }

    private static void RestoreStartScene(UnityEditor.PlayModeStateChange state)
    {
        if (state != UnityEditor.PlayModeStateChange.EnteredEditMode ||
            !UnityEditor.SessionState.GetBool(OverrideActive, false))
            return;

        string previous = UnityEditor.SessionState.GetString(PreviousScene, "");
        UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(previous)
            ? null : UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.SceneAsset>(previous);
        UnityEditor.SessionState.EraseBool(OverrideActive);
        UnityEditor.SessionState.EraseString(PreviousScene);
    }
}
#endif
