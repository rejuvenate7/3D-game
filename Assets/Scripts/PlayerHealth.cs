using UnityEngine;

public class PlayerHealth : MonoBehaviour
{
    public float totalHealth = 100f;
    public float currentHealth;
    
    void Start()
    {
        currentHealth = totalHealth;
        }
}


