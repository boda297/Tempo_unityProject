using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// EDITOR ONLY - put this file in Assets/Editor/.
/// Menu: Tools > Tempo > Build Player HUD
///
/// Builds:
///   PlayerUIManager  (Canvas Overlay, Canvas Scaler 1920x1080, Graphic Raycaster, PlayerUIManager.cs)
///   └── HUDManager   (PlayerHUD.cs)
///       └── StatusBars
///           ├── HealthBar   (Slider, display-only, 36px)
///           └── StaminaBar  (Slider, display-only, 24px)
///
/// If a PlayerUIManager already exists in the open scene, it is reused (missing components are added).
/// Run it in the TITLE scene - PlayerUIManager is DontDestroyOnLoad, so the HUD carries into the world.
/// </summary>
public static class PlayerHUDBuilder
{
    [MenuItem("Tools/Tempo/Build Player HUD")]
    public static void Build()
    {
        // ---- Root: PlayerUIManager -------------------------------------------------
        var existing = Object.FindFirstObjectByType<PlayerUIManager>();
        GameObject root;

        if (existing != null)
        {
            root = existing.gameObject;
        }
        else
        {
            root = new GameObject("PlayerUIManager");
            Undo.RegisterCreatedObjectUndo(root, "Build Player HUD");
            Undo.AddComponent<PlayerUIManager>(root);
        }

        if (root.transform.Find("HUDManager") != null)
        {
            Debug.LogWarning("[PlayerHUDBuilder] HUDManager already exists under PlayerUIManager - nothing built.");
            Selection.activeGameObject = root;
            return;
        }

        var canvas = root.GetComponent<Canvas>();
        if (canvas == null) canvas = Undo.AddComponent<Canvas>(root); // converts Transform -> RectTransform
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        var scaler = root.GetComponent<CanvasScaler>();
        if (scaler == null) scaler = Undo.AddComponent<CanvasScaler>(root);
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        if (root.GetComponent<GraphicRaycaster>() == null)
            Undo.AddComponent<GraphicRaycaster>(root);

        // ---- HUDManager -----------------------------------------------------------
        var hudManager = new GameObject("HUDManager", typeof(RectTransform));
        hudManager.transform.SetParent(root.transform, false);
        Stretch(hudManager.GetComponent<RectTransform>());
        var hud = hudManager.AddComponent<PlayerHUD>();

        // ---- StatusBars (bottom-left, vertical stack) -----------------------------
        var statusBars = new GameObject("StatusBars", typeof(RectTransform));
        statusBars.transform.SetParent(hudManager.transform, false);
        var sbRect = statusBars.GetComponent<RectTransform>();
        sbRect.anchorMin = sbRect.anchorMax = new Vector2(0f, 1f); // top-left
        sbRect.pivot = new Vector2(0f, 1f);
        sbRect.anchoredPosition = new Vector2(40f, -40f);
        sbRect.sizeDelta = new Vector2(480f, 72f);

        var layout = statusBars.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 12f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var healthBar = CreateBar("HealthBar", statusBars.transform, new Color(0.85f, 0.15f, 0.15f, 1f), 36f);
        var staminaBar = CreateBar("StaminaBar", statusBars.transform, new Color(0.2f, 0.8f, 0.3f, 1f), 24f);

        // ---- Wire PlayerHUD's serialized fields -----------------------------------
        var so = new SerializedObject(hud);
        so.FindProperty("statusBarsRoot").objectReferenceValue = statusBars;
        so.FindProperty("healthSlider").objectReferenceValue = healthBar;
        so.FindProperty("staminaSlider").objectReferenceValue = staminaBar;
        so.ApplyModifiedPropertiesWithoutUndo();

        Undo.RegisterCreatedObjectUndo(hudManager, "Build Player HUD");
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;

        Debug.Log("[PlayerHUDBuilder] Player HUD built. Save the scene.");
    }

    // ---------------------------------------------------------------------------
    private static Slider CreateBar(string name, Transform parent, Color fillColor, float height)
    {
        var barGO = new GameObject(name, typeof(RectTransform), typeof(Slider), typeof(LayoutElement));
        barGO.transform.SetParent(parent, false);
        barGO.GetComponent<LayoutElement>().preferredHeight = height;

        // Background
        var bg = CreateImage("Background", barGO.transform, new Color(0f, 0f, 0f, 0.6f));
        Stretch(bg.rectTransform);

        // Fill area + fill (the Slider drives the fill's anchors)
        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(barGO.transform, false);
        Stretch(fillArea.GetComponent<RectTransform>());

        var fill = CreateImage("Fill", fillArea.transform, fillColor);
        var fillRect = fill.rectTransform;
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;

        // Slider configured as a pure display bar (no handle, not interactable)
        var slider = barGO.GetComponent<Slider>();
        slider.fillRect = fillRect;
        slider.handleRect = null;
        slider.targetGraphic = null;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 100f;
        slider.wholeNumbers = false;
        slider.value = 100f;
        slider.interactable = false;
        slider.transition = Selectable.Transition.None;

        var nav = slider.navigation;
        nav.mode = Navigation.Mode.None;
        slider.navigation = nav;

        return slider;
    }

    private static Image CreateImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}