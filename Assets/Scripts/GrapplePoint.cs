using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class GrapplePoint : MonoBehaviour
{
    [SerializeField] private Transform anchor;
    public Vector3 Position => anchor != null ? anchor.position : transform.position;
    public static readonly List<GrapplePoint> Active = new List<GrapplePoint>();
    private void OnEnable() { if (!Active.Contains(this)) Active.Add(this); }
    private void OnDisable() { Active.Remove(this); }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(Position, 0.35f);
    }
}
