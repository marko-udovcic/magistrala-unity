using UnityEngine;

/// <summary>Small runtime probe readout; does not require a Canvas or any UI packages.</summary>
[DisallowMultipleComponent]
public sealed class TruckHud : MonoBehaviour
{
    public TruckController target;
    float smoothedFrameTime = 1f / 60f;
    GUIStyle labelStyle, helpStyle, gameStyle;

    void Awake()
    {
        if (!target) target = FindFirstObjectByType<TruckController>();
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        if (dt > 0) smoothedFrameTime = Mathf.Lerp(smoothedFrameTime, dt, 1f - Mathf.Exp(-dt * 3f));
        if (!target) target = FindFirstObjectByType<TruckController>();
    }

    void OnGUI()
    {
        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } };
            helpStyle = new GUIStyle(labelStyle) { fontSize = 13 };
        }
        float width = Mathf.Min(1000f, Screen.width - 16f);
        // the game's keys that apply right now (MagistralaGame: tours, unload, phone, payout card) on their own line above
        string game = Magistrala.Game.MagistralaGame.I ? Magistrala.Game.MagistralaGame.I.HelpKeys : null;
        if (!string.IsNullOrEmpty(game))
        {
            gameStyle ??= new GUIStyle(labelStyle) { fontSize = 15, fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.84f, 0.3f) } };
            Rect gp = new Rect(8, Screen.height - 98 - 30, width, 28);
            GUI.Box(gp, GUIContent.none);
            GUI.Label(new Rect(gp.x + 10, gp.y + 2, width - 20, 24), "Igra:  " + game, gameStyle);
        }
        Rect panel = new Rect(8, Screen.height - 98, width, 90);
        GUI.Box(panel, GUIContent.none);
        string gear = !target || target.Gear == 0 ? "N" : target.Gear < 0 ? "R" : target.Gear.ToString();
        float speed = target ? target.SpeedKmh : 0;
        float rpm = target ? target.EngineRpm : 0;
        GUI.Label(new Rect(panel.x + 10, panel.y + 4, width - 20, 28), $"{speed:0} km/h    Brzina: {gear}    {rpm:0} rpm", labelStyle);
        float ms = smoothedFrameTime * 1000f;
        GUI.Label(new Rect(panel.x + 10, panel.y + 33, width - 20, 22), $"FPS: {1f / Mathf.Max(smoothedFrameTime, 0.0001f):0} ({ms:0.0} ms) · kvaliteta {Magistrala.RenderQuality.Label}", helpStyle);
        GUI.Label(new Rect(panel.x + 10, panel.y + 58, width - 20, 23), (target && target.parkLock ? "RUČNA (istovar) · " : "") + "WASD vozi · Space ručna · C kabina · R vrati · miš kamera · G zalazak · L svjetla · P autopilot · K kvaliteta · F1 graf · F2 konzola", helpStyle);
    }
}
