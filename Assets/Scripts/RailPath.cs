using System.Collections.Generic;
using UnityEngine;


[DisallowMultipleComponent]
public class RailPath : MonoBehaviour
{
    public Vector3[] points = { Vector3.zero, new Vector3(0f, 0f, 15f) };
    [Min(0.1f)] public float captureRadius = 1.5f;
    [SerializeField, HideInInspector] private int captureSettingsVersion;
    [Min(0.05f)] public float width = 0.25f;
    [Min(0.05f)] public float thickness = 0.2f;
    public Material railMaterial;
    public static readonly List<RailPath> Active = new List<RailPath>();

    private void OnEnable() { UpgradeCaptureSettings(); if (!Active.Contains(this)) Active.Add(this); }
    private void OnDisable() { Active.Remove(this); }
    private void OnValidate() { UpgradeCaptureSettings(); }
    private void UpgradeCaptureSettings()
    {
        if (captureSettingsVersion >= 1) return;
        captureRadius = Mathf.Max(captureRadius, 1.5f);
        captureSettingsVersion = 1;
    }

    public float Length
    {
        get
        {
            float length = 0f;
            if (points == null) return length;
            for (int i = 1; i < points.Length; i++)
                length += Vector3.Distance(transform.TransformPoint(points[i - 1]),
                                           transform.TransformPoint(points[i]));
            return length;
        }
    }

    public bool Closest(Vector3 position, out float distance, out Vector3 point)
    {
        distance = 0f;
        point = transform.position;
        float best = float.PositiveInfinity;
        float travelled = 0f;
        if (points == null) return false;
        for (int i = 1; i < points.Length; i++)
        {
            Vector3 a = transform.TransformPoint(points[i - 1]);
            Vector3 delta = transform.TransformPoint(points[i]) - a;
            float length = delta.magnitude;
            if (length < 0.001f) continue;
            float t = Mathf.Clamp01(Vector3.Dot(position - a, delta) / (length * length));
            Vector3 candidate = a + delta * t;
            float error = (position - candidate).sqrMagnitude;
            if (error < best)
            {
                best = error;
                point = candidate;
                distance = travelled + length * t;
            }
            travelled += length;
        }
        return !float.IsPositiveInfinity(best);
    }

    public void Sample(float distance, out Vector3 point, out Vector3 tangent)
    {
        point = transform.position;
        tangent = transform.forward;
        if (points == null) return;
        distance = Mathf.Max(0f, distance);
        for (int i = 1; i < points.Length; i++)
        {
            Vector3 a = transform.TransformPoint(points[i - 1]);
            Vector3 delta = transform.TransformPoint(points[i]) - a;
            float length = delta.magnitude;
            if (length < 0.001f) continue;
            tangent = delta / length;
            point = a + tangent * Mathf.Min(distance, length);
            if (distance <= length) return;
            distance -= length;
        }
    }


    [ContextMenu("Rebuild Rail Geometry")]
    public void RebuildGeometry()
    {
        Transform old = transform.Find("__RailGeometry");
        if (old != null)
        {
            old.gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(old.gameObject);
            else DestroyImmediate(old.gameObject);
        }
        GameObject root = new GameObject("__RailGeometry");
        root.transform.SetParent(transform, false);
        if (points == null) return;
        for (int i = 1; i < points.Length; i++)
        {
            Vector3 delta = points[i] - points[i - 1];
            float length = delta.magnitude;
            if (length < 0.001f) continue;
            GameObject segment = GameObject.CreatePrimitive(PrimitiveType.Cube);
            segment.name = "Rail segment " + i;
            segment.layer = gameObject.layer;
            segment.transform.SetParent(root.transform, false);
            segment.transform.localRotation = Quaternion.LookRotation(delta.normalized, Vector3.up);

            Vector3 up = segment.transform.localRotation * Vector3.up;
            segment.transform.localPosition = (points[i - 1] + points[i]) * 0.5f - up * thickness * 0.5f;
            segment.transform.localScale = new Vector3(width, thickness, length + 0.02f);
            if (railMaterial != null) segment.GetComponent<Renderer>().sharedMaterial = railMaterial;
        }
    }

    private void OnDrawGizmos()
    {
        if (points == null) return;
        Gizmos.color = Color.yellow;
        for (int i = 0; i < points.Length; i++)
        {
            Vector3 p = transform.TransformPoint(points[i]);
            Gizmos.DrawWireSphere(p, 0.12f);
            if (i > 0) Gizmos.DrawLine(transform.TransformPoint(points[i - 1]), p);
        }
    }
}
