using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class RopeTether : MonoBehaviour
{
    [SerializeField] private Material ropeMaterial;
    [SerializeField, Range(6, 48)] private int segments = 24;
    [SerializeField, Min(0.005f)] private float width = 0.045f;
    [SerializeField, Range(4, 32)] private int constraintIterations = 16;
    private LineRenderer line;
    private Material ownedMaterial;
    private Vector3[] nodes;
    private Vector3[] previous;
    private bool visible;
    private float accumulator;
    private Vector3 lastStart;
    private Vector3 lastEnd;
    private const float Step = 1f / 120f;

    public void SetMaterial(Material material) { ropeMaterial = material; }

    private void EnsureLine()
    {
        if (line != null) return;
        GameObject child = new GameObject("Grapple Tether");
        child.transform.SetParent(transform, false);
        line = child.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.startWidth = line.endWidth = width;
        line.numCapVertices = 4;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.startColor = line.endColor = Color.white;
        if (ropeMaterial == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                ownedMaterial = new Material(shader);
                if (ownedMaterial.HasProperty("_BaseColor"))
                    ownedMaterial.SetColor("_BaseColor", Color.cyan);
                else ownedMaterial.color = Color.cyan;
            }
        }
        line.sharedMaterial = ropeMaterial != null ? ropeMaterial : ownedMaterial;
    }

    public void Draw(Vector3 start, Vector3 end, float ropeLength, float deltaTime)
    {
        EnsureLine();
        int count = Mathf.Clamp(segments, 6, 48) + 1;
        if (!visible || nodes == null || nodes.Length != count ||
            (start - lastStart).sqrMagnitude > 100f || (end - lastEnd).sqrMagnitude > 100f)
        {
            nodes = new Vector3[count];
            previous = new Vector3[count];
            for (int i = 0; i < count; i++)
                previous[i] = nodes[i] = Vector3.Lerp(start, end, i / (float)(count - 1));
            accumulator = 0f;
            lastStart = start;
            lastEnd = end;
        }
        visible = true;
        line.enabled = true;

        float totalLength = Mathf.Max(ropeLength, Vector3.Distance(start, end));
        float rest = totalLength / (count - 1);
        float duration = Mathf.Min(deltaTime, 0.08f);
        accumulator += duration;
        int steps = Mathf.FloorToInt(accumulator / Step);
        for (int step = 0; step < steps; step++)
        {
            float t = (step + 1f) / steps;
            Vector3 pinStart = Vector3.Lerp(lastStart, start, t);
            Vector3 pinEnd = Vector3.Lerp(lastEnd, end, t);
            for (int i = 1; i < count - 1; i++)
            {
                Vector3 current = nodes[i];
                nodes[i] += (nodes[i] - previous[i]) * 0.99f + Vector3.down * (15f * Step * Step);
                previous[i] = current;
            }
            for (int iteration = 0; iteration < constraintIterations; iteration++)
            {
                nodes[0] = pinStart;
                nodes[count - 1] = pinEnd;
                for (int i = 0; i < count - 1; i++)
                {
                    Vector3 delta = nodes[i + 1] - nodes[i];
                    float length = delta.magnitude;
                    if (length < 0.0001f) continue;
                    Vector3 correction = delta * ((length - rest) / length);
                    if (i == 0) nodes[i + 1] -= correction;
                    else if (i + 1 == count - 1) nodes[i] += correction;
                    else { nodes[i] += correction * 0.5f; nodes[i + 1] -= correction * 0.5f; }
                }
            }
            accumulator -= Step;
        }
        nodes[0] = start;
        nodes[count - 1] = end;

        float taut = Mathf.InverseLerp(totalLength * 0.9f, totalLength, Vector3.Distance(start, end));
        line.positionCount = count;
        for (int i = 0; i < count; i++)
            line.SetPosition(i, Vector3.Lerp(nodes[i], Vector3.Lerp(start, end, i / (float)(count - 1)), taut));
        lastStart = start;
        lastEnd = end;
    }

    public void Release()
    {
        visible = false;
        if (line != null) line.enabled = false;
    }

    private void OnDisable() { Release(); }
    private void OnDestroy() { if (ownedMaterial != null) Destroy(ownedMaterial); }
}
