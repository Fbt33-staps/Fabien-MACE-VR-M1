using UnityEngine;
using UnityEngine.XR; // Indispensable pour détecter les manettes VR

public class DisqueCollision : MonoBehaviour
{
    // --- VARIABLES PUBLIQUES (Remplies automatiquement par le Manager lors du Spawn) ---
    [Header("Paramètres reçus du Manager")]
    public bool modeReaching;      // Est-ce qu'on doit toucher (Vrai) ou cliquer (Faux) ?
    public int couleurID;          // 0=Jaune, 1=Bleu, 2=Rouge
    public string nomCondition;    // "JAUNE", "BLEU" ou "ROUGE"
    public string mainAttendue;    // "MAIN_DROITE", "MAIN_GAUCHE" ou "AUCUNE"
    public StimulusController manager; // Lien vers le cerveau de l'expérience

    // --- VARIABLES INTERNES ---
    private bool aRepondu = false; // Pour éviter de répondre 2 fois sur le même essai
    private float tempsApparition; // L'heure exacte où la balle est apparue

    void Start()
    {
        // 1. On note l'heure de naissance de la balle
        tempsApparition = Time.time;

        // 2. Sécurité : On lance un compte à rebours de 2 secondes.
        // Si le joueur ne fait rien pendant 2s, la fonction "TropTard" se lancera.
        Invoke("TropTard", 2.0f);
    }

    void Update()
    {
        // Si le joueur a déjà répondu, on ne fait plus rien
        if (aRepondu) return;

        // --- GESTION MODE GACHETTE (Statique) ---
        // Ce bloc ne s'exécute QUE si la case "Mode Reaching" est DECOCHÉE
        if (!modeReaching)
        {
            // On vérifie les boutons "Trigger" des manettes VR
            // (J'ai laissé les flèches clavier pour tester sans casque)
            bool triggerG = IsTriggerPressed(XRNode.LeftHand) || Input.GetKeyDown(KeyCode.LeftArrow);
            bool triggerD = IsTriggerPressed(XRNode.RightHand) || Input.GetKeyDown(KeyCode.RightArrow);

            // Si au moins un bouton est pressé
            if (triggerG || triggerD)
            {
                string action = "INCONNU";
                if (triggerG) action = "MAIN_GAUCHE";
                if (triggerD) action = "MAIN_DROITE";
                if (triggerG && triggerD) action = "LES_DEUX"; // Cas très rare

                AnalyserReponse(action);
            }
        }
    }

    // --- GESTION MODE REACHING (Mouvement) ---
    // Cette fonction Unity se déclenche automatiquement quand un objet physique touche la balle
    void OnTriggerEnter(Collider other)
    {
        // On ignore la collision si :
        // 1. Le joueur a déjà répondu
        // 2. On est en mode Gâchette (car on ne veut pas de collision physique)
        if (aRepondu || !modeReaching) return;

        string action = "INCONNU";

        // On vérifie le TAG de l'objet qui nous touche
        if (other.CompareTag("MainGauche")) action = "MAIN_GAUCHE";
        else if (other.CompareTag("MainDroite")) action = "MAIN_DROITE";
        else return; // Si c'est le mur, le sol ou la tête, on ignore et on continue

        // Si c'est bien une main, on analyse !
        AnalyserReponse(action);
    }

    // --- CERVEAU DU DISQUE : ANALYSE ---
    void AnalyserReponse(string actionJoueur)
    {
        aRepondu = true;
        CancelInvoke("TropTard"); // IMPORTANT : On arrête le chrono de sécurité car le joueur a réagi

        // Calcul du Temps de Réaction (Temps actuel - Temps début)
        float trt = Time.time - tempsApparition;
        string resultat = "ERREUR";

        // LOGIQUE DE VALIDATION
        if (couleurID == 0) // CIBLE JAUNE (Attend Droite)
        {
            if (actionJoueur == "MAIN_DROITE") resultat = "SUCCES";
            else resultat = "ECHEC_MAUVAIS_COTE";
        }
        else if (couleurID == 1) // CIBLE BLEUE (Attend Gauche)
        {
            if (actionJoueur == "MAIN_GAUCHE") resultat = "SUCCES";
            else resultat = "ECHEC_MAUVAIS_COTE";
        }
        else // CIBLE ROUGE (No-Go / Interdit)
        {
            // Si on est ici, c'est qu'on a bougé alors qu'il fallait rester immobile
            resultat = "ECHEC_COMMISSION";
        }

        Debug.Log($"Résultat: {resultat} | TRT: {trt:F4}");

        // --- ENVOI AU MANAGER ---
        // C'est ici que la magie opère. On dit au Manager : "C'est fini !"
        // Le Manager va recevoir ça et se dire : "Ok, maintenant j'allume la Zone de Départ".
        if (manager != null)
        {
            manager.FinEssai(nomCondition, mainAttendue, actionJoueur, resultat, trt);
        }

        // La balle a fini son travail, elle disparaît.
        Destroy(gameObject);
    }

    // --- GESTION DU TEMPS ECOULÉ (TIMEOUT) ---
    void TropTard()
    {
        if (aRepondu) return;
        aRepondu = true;

        float trt = 2.0f; // On note le temps max
        string actionJoueur = "AUCUNE"; // Le joueur n'a rien fait

        string resultat = "";

        // CAS SPECIAL : Si c'était ROUGE (No-Go), ne rien faire est une victoire !
        if (couleurID == 2)
        {
            resultat = "SUCCES_NOGO";
        }
        else // Sinon, c'est un échec par omission (trop lent ou endormi)
        {
            resultat = "ECHEC_OMISSION";
        }

        if (manager != null)
        {
            manager.FinEssai(nomCondition, mainAttendue, actionJoueur, resultat, trt);
        }

        Destroy(gameObject);
    }

    // --- UTILITAIRE VR ---
    // Petite fonction pour vérifier proprement si le bouton Trigger est appuyé
    bool IsTriggerPressed(XRNode node)
    {
        InputDevice device = InputDevices.GetDeviceAtXRNode(node);
        bool val = false;
        if (device.isValid)
        {
            // On demande à la manette : "Est-ce que le trigger est appuyé ?"
            device.TryGetFeatureValue(CommonUsages.triggerButton, out val);
        }
        return val;
    }
}