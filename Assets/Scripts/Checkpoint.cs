using UnityEngine;

[RequireComponent(typeof(Collider))]
public class Checkpoint : MonoBehaviour
{
    private void Awake()
    {
        Collider col = GetComponent<Collider>();
        col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player") || other.GetComponent<PlayerMovement>() != null)
        {
            // Save this checkpoint's position (or a specific spawn anchor child transform if desired)
            Vector3 spawnPos = transform.position;
            Quaternion spawnRot = transform.rotation;

            if (CheckpointManager.Instance != null)
            {
                CheckpointManager.Instance.SetCheckpoint(spawnPos, spawnRot);
            }
        }
    }
}