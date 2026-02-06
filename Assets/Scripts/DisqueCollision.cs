using UnityEngine;
using UnityEngine.XR;

public class DisqueCollision : MonoBehaviour
{
    public bool modeReaching;
    public int couleurID;
    public string nomCondition;
    public string mainAttendue;
    public StimulusController manager;

    public GameObject iconeSucces;   
    public GameObject iconeEchec;    
    public Renderer monRenderer;     

    private bool aRepondu = false;
    private float tempsApparition;

    void Start()
    {
        tempsApparition = Time.time;
        if (monRenderer == null) monRenderer = GetComponent<Renderer>(); 
        Invoke("TropTard", 2.0f);
    }

    void Update()
    {
        if (aRepondu) return;
        if (!modeReaching)
        {
            bool triggerG = IsTriggerPressed(XRNode.LeftHand) || Input.GetKeyDown(KeyCode.LeftArrow);
            bool triggerD = IsTriggerPressed(XRNode.RightHand) || Input.GetKeyDown(KeyCode.RightArrow);
            if (triggerG || triggerD)
            {
                string action = "INCONNU";
                if (triggerG) action = "MAIN_GAUCHE";
                if (triggerD) action = "MAIN_DROITE";
                if (triggerG && triggerD) action = "LES_DEUX"; 
                AnalyserReponse(action);
            }
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (aRepondu || !modeReaching) return;
        string action = "INCONNU";
        if (other.CompareTag("MainGauche")) action = "MAIN_GAUCHE";
        else if (other.CompareTag("MainDroite")) action = "MAIN_DROITE";
        else return;
        AnalyserReponse(action);
    }

    void AnalyserReponse(string actionJoueur)
    {
        aRepondu = true;
        CancelInvoke("TropTard");
        float trt = Time.time - tempsApparition;
        string resultat = "ERREUR";

        if (couleurID == 0) { if (actionJoueur == "MAIN_DROITE") resultat = "SUCCES"; else resultat = "ECHEC_MAUVAIS_COTE"; }
        else if (couleurID == 1) { if (actionJoueur == "MAIN_GAUCHE") resultat = "SUCCES"; else resultat = "ECHEC_MAUVAIS_COTE"; }
        else { resultat = "ECHEC_COMMISSION"; }

        Envoyer(resultat, actionJoueur, trt);
    }

    void TropTard()
    {
        if (aRepondu) return;
        aRepondu = true;
        float trt = 2.0f;
        string actionJoueur = "AUCUNE";
        string resultat = (couleurID == 2) ? "SUCCES_NOGO" : "ECHEC_OMISSION";
        Envoyer(resultat, actionJoueur, trt);
    }

    void Envoyer(string resultat, string actionJoueur, float trt)
    {
        if (manager != null) manager.FinEssai(nomCondition, mainAttendue, actionJoueur, resultat, trt);
        
        bool modeEntrainement = (manager != null && manager.estEnFamiliarisation);
        if (modeEntrainement)
        {
            if(monRenderer != null) monRenderer.enabled = false;
            if (resultat.Contains("SUCCES")) { if(iconeSucces != null) iconeSucces.SetActive(true); }
            else { if(iconeEchec != null) iconeEchec.SetActive(true); }
            Destroy(gameObject, 1.0f);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    bool IsTriggerPressed(XRNode node)
    {
        InputDevice device = InputDevices.GetDeviceAtXRNode(node);
        bool val = false;
        if (device.isValid) device.TryGetFeatureValue(CommonUsages.triggerButton, out val);
        return val;
    }
}