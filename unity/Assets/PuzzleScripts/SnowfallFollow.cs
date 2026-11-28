using UnityEngine;

// Keeps a snowfall particle emitter above this player's camera while the camera is over the snowy area, and
// parks it far below the world otherwise. Snow is only ever simulated around each player, never over the whole
// island. Nothing here is shared between players: each PC runs its own flakes.
//
// This runs inside the game (IL2CPP), where engine methods the game itself never calls do not exist. Keep to
// Camera.main, Transform and plain arithmetic.
public class SnowfallFollow : MonoBehaviour
{
    [SerializeField] private float height = 11f;            // how far above the camera the flakes start
    [SerializeField] private Vector3 areaCentre;            // world centre of the snowy area
    [SerializeField] private Vector3 areaSize;              // its size in x and z (y unused)
    [SerializeField] private float margin = 70f;            // how far outside the area it still snows
    void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 p = cam.transform.position;
        float dx = p.x - areaCentre.x, dz = p.z - areaCentre.z;
        if (dx < 0f) dx = -dx;
        if (dz < 0f) dz = -dz;
        bool inside = dx < areaSize.x * 0.5f + margin && dz < areaSize.z * 0.5f + margin;
        transform.position = inside ? new Vector3(p.x, p.y + height, p.z) : new Vector3(p.x, -600f, p.z);
    }
}
