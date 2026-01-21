using UnityEngine;

public class ZoneDepart : MonoBehaviour
{
    [Header("Lier le GameManager ici")]
    public StimulusController manager;

    // Cette fonction se déclenche quand un objet ENTRE dans le cylindre
    void OnTriggerEnter(Collider other)
    {
        // On vérifie que c'est bien une main (grâce aux Tags Unity)
        if (other.CompareTag("MainGauche") || other.CompareTag("MainDroite"))
        {
            if (manager != null)
            {
                // On envoie l'info au cerveau : "La Main est revenue !"
                manager.JoueurEstRevenu(other.gameObject.tag);
            }
        }
    }
}