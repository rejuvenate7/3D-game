using UnityEngine;

public class CheckpointManager : MonoBehaviour
{
    public static CheckpointManager Instance { get; private set; }

    [Header("Current Checkpoint Data")]
    [SerializeField] private Vector3 currentCheckpointPosition;
    [SerializeField] private Quaternion currentCheckpointRotation;
    private bool hasCheckpoint = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void SetCheckpoint(Vector3 position, Quaternion rotation)
    {
        currentCheckpointPosition = position;
        currentCheckpointRotation = rotation;
        hasCheckpoint = true;
        Debug.Log("Checkpoint saved at: " + position);
    }

    public bool TryGetCheckpoint(out Vector3 position, out Quaternion rotation)
    {
        position = currentCheckpointPosition;
        rotation = currentCheckpointRotation;
        return hasCheckpoint;
    }
}