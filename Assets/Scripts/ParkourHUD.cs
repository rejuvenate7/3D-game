using UnityEngine;

[RequireComponent(typeof(PlayerMovement))]
public class ParkourHUD : MonoBehaviour
{
    private PlayerMovement player;
    private void Awake() { player = GetComponent<PlayerMovement>(); }
    private void OnGUI()
    {
        if (player == null || !player.isActiveAndEnabled) return;
        Color previous = GUI.color;
        GUI.color = player.CanGrapple ? Color.cyan : Color.white;
        GUI.Label(new Rect(Screen.width * 0.5f - 4f, Screen.height * 0.5f - 10f, 20f, 25f), "+");
        GUI.color = Color.white;
        GUI.Box(new Rect(10f, 10f, 540f, 75f), "");
        GUI.Label(new Rect(20f, 15f, 520f, 25f), player.Mode + "   |   " + player.Speed.ToString("F1") + " m/s" +
                  (player.IsGrappling ? "   |   Rope: " + player.RopeLength.ToString("F1") + " m" : ""));
        GUI.Label(new Rect(20f, 38f, 520f, 25f), "WASD move | Space jump / hold to wall run | C slide");
        GUI.Label(new Rect(20f, 58f, 520f, 25f), "E attach | E / Space release | WASD swing | Esc cursor");
        GUI.color = previous;
    }
}
