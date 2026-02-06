using UnityEngine;

public class Billboard : MonoBehaviour
{
    void Update()
    {
        // Trouve la caméra principale (celle du casque VR)
        Camera cam = Camera.main;
        
        if (cam != null)
        {
            // Force l'objet à regarder dans la même direction que la caméra
            // (L'astuce est de regarder "vers l'arrière" de l'objet pour qu'il soit face à nous)
            transform.LookAt(transform.position + cam.transform.rotation * Vector3.forward,
                             cam.transform.rotation * Vector3.up);
        }
    }
}