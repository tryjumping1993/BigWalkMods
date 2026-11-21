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
    // The mine: no snow falls underground. Its hall's centre (y: the mine's floor), and each tunnel as (x of its
    // direction, its length from the hall's centre, z of its direction)
    [SerializeField] private Vector3 mineHub;
    [SerializeField] private Vector3 mineA, mineB, mineC;

    bool InTunnel(Vector3 p, Vector3 t)
    {
        float along = (p.x - mineHub.x) * t.x + (p.z - mineHub.z) * t.z, across = (p.x - mineHub.x) * t.z - (p.z - mineHub.z) * t.x;
        if (across < 0f) across = -across;
        return along > 0f && along < t.y && across < 5.5f;
    }

    void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 p = cam.transform.position;
        float dx = p.x - areaCentre.x, dz = p.z - areaCentre.z;
        if (dx < 0f) dx = -dx;
        if (dz < 0f) dz = -dz;
        bool inside = dx < areaSize.x * 0.5f + margin && dz < areaSize.z * 0.5f + margin;
        if (inside && p.y > mineHub.y - 2f && p.y < mineHub.y + 14f)
        {
            float hx = p.x - mineHub.x, hz = p.z - mineHub.z;
            if (hx * hx + hz * hz < 30f * 30f || InTunnel(p, mineA) || InTunnel(p, mineB) || InTunnel(p, mineC)) inside = false;
        }
        transform.position = inside ? new Vector3(p.x, p.y + height, p.z) : new Vector3(p.x, -600f, p.z);
    }
}
