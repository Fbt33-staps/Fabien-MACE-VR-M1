using UnityEngine;
using UnityEngine.XR; 
using System.Collections;
using System.Collections.Generic; 
using System.IO; 
using System;
using System.Globalization; 

public class StimulusController : MonoBehaviour
{
    [Header("Interface Visuelle")]
    public TextMesh afficheurTexte; 
    public GameObject croixFixation; 
    public GameObject zoneDepart; 
    
    [Header("Feedback Erreur")]
    public GameObject feedbackErreurTete; 

    // --- CONFIGURATION ---
    private GameConfig maConfig; 

    [Header("Paramètres Sujet")]
    public string idSujet; 
    public string niveau;   
    
    [Tooltip("Cocher pour le mode Mouvement. Décocher pour Gachette.")]
    public bool modeReaching; 

    public enum TypePhase { Phase1_MainDroite, Phase2_MainGauche, Phase3_Decision }
    
    [Header("État (Lecture Seule)")]
    public TypePhase phaseActuelle; // Pour debug
    private TypePhase phaseSuivante; 
    
    public bool estEnFamiliarisation;
    private int compteurEssaiPhase;  // 6 en fam et 12 en serie

    /*
    if compteurEssaiPhase < nbEssaiPhase:
        je fais mon essai
    sinon
        je passe à la suite
        si je suis en fam et que compteur serie < nbSerie:
            alors je fais ma serie suivante
    */
    private int compteurSerie; // 3 max
    
    private int compteurEchecsPhase; 
    private int compteurErreursPartiellesPhase; 
    
    private bool enAttenteDeDemarrage = false; 
    private bool enAttenteDeRetourMains = false; 
    private string tagMainQuiDoitRevenir; 
    
    private GameObject currentStimulus; 

    // --- CINEMATIQUE & POSTURE ---
    private bool enregistrementEnCours = false;
    private List<string> bufferCinematique = new List<string>(); 
    private float tempsDebutEssai; 

    // Anti-Triche Tête
    private Vector3 positionTeteDepart; 
    
    [Header("Réglages Anti-Triche Tête")]
    [Tooltip("Distance max autorisée pour le mouvement de tête (en mètres). Ex: 0.1 = 10cm")]
    [Range(0.01f, 0.5f)] 
    public float toleranceMouvementTete = 0.08f; 

    // --- DETECTION ERREUR PARTIELLE ---
    [Header("Paramètres Détection Mains")]
    private float seuilDetectionMouvement = 0.05f; 
    private Vector3 posDepartMainG;
    private Vector3 posDepartMainD;
    private bool erreurPartielleCeTour = false; 
    private string mainQuiNeDoitPasBouger = ""; 

    private GameObject objMainGauche;
    private GameObject objMainDroite;

    // --- VOLUMETRIE ---
    private int nbEssaisFamil = 6;
    private int nbEssaisMesure = 36; 
    private int tailleVague = 12; // serie

    [Header("Temps de Pause")]
    public float dureePauseVague = 15.0f;
    public float dureePauseBloc = 60.0f;
    
    private float delaiMinCurrent;
    private float delaiMaxCurrent;

    // --- MATERIEL ---
    [Header("Références Unity")]
    public GameObject stimulusPrefab;
    public Transform cameraVR;
    
    [Header("Position de la Cible (MANUEL)")]
    [Tooltip("Distance de la cible par rapport à la zone de départ (en mètres). Ex: 0.3 = 30cm")]
    [Range(0.2f, 1.0f)] // Curseur pour tester facilement
    private float distanceCible = 0.3f; 
    
    // Calibration Zone Départ
    private float calibrationOffsetX = -0.05f; 
    private float distanceDevant = 0.2f; 
    private float profondeurZone = 0.5f; 

    [Header("Couleurs")]
    public Material matJaune; 
    public Material matBleu;  
    public Material matRouge; 

    private string cheminFichierResultats;
    private string cheminFichierKinematics;

    void Start()
    {
        PreparerFichiersCSV(); 

        // Récupérer les manettes
        objMainGauche = GameObject.FindGameObjectWithTag("MainGauche");
        objMainDroite = GameObject.FindGameObjectWithTag("MainDroite");

        // Initialisation comme gameobject inactif
        croixFixation.SetActive(false);
        zoneDepart.SetActive(false);
        feedbackErreurTete.SetActive(false); 


        tagMainQuiDoitRevenir = "TOUTES";
        phaseSuivante = TypePhase.Phase1_MainDroite; 
        
        enAttenteDeDemarrage = true; 

        string modeTexte = modeReaching ? "REACHING (Mouvement)" : "GACHETTE (Statique)";
        string msgAccueil = $"ID : {idSujet} | Niv : {niveau}\nMode : {modeTexte}\n\nRegardez DROIT DEVANT\net Appuyez sur ENTREE";

        afficheurTexte.text = msgAccueil;
    }

    void Update()
    {
        if (enAttenteDeDemarrage && Input.GetKeyDown(KeyCode.Return))
        {
            CalibrerZoneDepart();
            enAttenteDeDemarrage = false; 
            LancerPhase(phaseSuivante);
        }
        
        if (enregistrementEnCours && modeReaching)
        {
            EnregistrerFrameCinematique();
            SurveillerErreurPartielle(); 
            SurveillerPostureTete(); 
        }
    }

    // --- SPAWN STIMULUS ---
    void SpawnStimulus()
    {
        if (currentStimulus != null) Destroy(currentStimulus);
        if(croixFixation != null) croixFixation.SetActive(false); 

        Vector3 origin = zoneDepart.transform.position;
        Vector3 directionDevant = zoneDepart.transform.up; 
        Vector3 directionCote = zoneDepart.transform.right; 

        // Utilise directement la variable 'distance' réglée dans l'inspecteur
        Vector3 pos = origin + (directionDevant * distanceCible);
        pos.y = cameraVR.position.y; 

        Quaternion rot = Quaternion.Euler(90, cameraVR.eulerAngles.y, 0);

        currentStimulus = Instantiate(stimulusPrefab, pos, rot);
        positionTeteDepart = cameraVR.position; 
        
        int choix = 0; 
        if (phaseActuelle == TypePhase.Phase1_MainDroite) choix = 0;
        else if (phaseActuelle == TypePhase.Phase2_MainGauche) choix = 1;
        else { float h = UnityEngine.Random.Range(0f, 100f); choix = (h < 25f) ? 2 : ((UnityEngine.Random.Range(0, 2) == 0) ? 0 : 1); }

        Renderer r = currentStimulus.GetComponent<Renderer>();
        string ns="", ma="";
        erreurPartielleCeTour = false;
        mainQuiNeDoitPasBouger = "AUCUNE_RESTRICTION";

        if (choix==0){ r.material=matJaune; ns="JAUNE"; ma="MAIN_DROITE"; mainQuiNeDoitPasBouger = "GAUCHE"; }
        else if (choix==1){ r.material=matBleu; ns="BLEU"; ma="MAIN_GAUCHE"; mainQuiNeDoitPasBouger = "DROITE"; }
        else{ r.material=matRouge; ns="ROUGE"; ma="AUCUNE"; mainQuiNeDoitPasBouger = "LES_DEUX"; }

        DisqueCollision script = currentStimulus.GetComponent<DisqueCollision>();
        if(script!=null){script.manager=this;script.modeReaching=modeReaching;script.couleurID=choix;script.nomCondition=ns;script.mainAttendue=ma;}

        if (modeReaching)
        {
            bufferCinematique.Clear(); tempsDebutEssai = Time.time;
            if (objMainGauche != null) posDepartMainG = objMainGauche.transform.position;
            if (objMainDroite != null) posDepartMainD = objMainDroite.transform.position;
            enregistrementEnCours = true;
        }
    }

    // --- SURVEILLANCE TETE ---
    void SurveillerPostureTete()
    {
        if (cameraVR == null) return;
        float dist = Vector3.Distance(cameraVR.position, positionTeteDepart);
        if (dist > toleranceMouvementTete) StartCoroutine(RoutineErreurPosture());
    }

    IEnumerator RoutineErreurPosture()
    {
        enregistrementEnCours = false;
        if (currentStimulus != null) Destroy(currentStimulus);
        if(croixFixation != null) croixFixation.SetActive(false);

        if (feedbackErreurTete != null) feedbackErreurTete.SetActive(true);
        yield return new WaitForSeconds(2.0f);
        if (feedbackErreurTete != null) feedbackErreurTete.SetActive(false);
        
        FinEssai("ERREUR", "AUCUNE", "TETE_BOUGE", "ECHEC_POSTURE", 0f);
    }

    // --- CALIBRATION ---
    void CalibrerZoneDepart()
    {
        // la direction exacte vers laquelle la caméra regarde, inclinaison comprise.
        Vector3 directionRegard = cameraVR.forward; 
        // On met la vericale à 0, au cas où la personne regarde vers le bas
        directionRegard.y = 0; 
        // Normalizes the magnitude of the current vector to 1 while maintaining the direction.
        directionRegard.Normalize();

        // //Où est la droite ? Mêmes étapes que forward
        // Vector3 droite = cameraVR.right; 
        // droite.y = 0; 
        // droite.Normalize();
        // Vector3 offsetLateral = droite * calibrationOffsetX;

        // Vector3 posPoitrine = cameraVR.position + (Vector3.down * 0.40f) + (directionRegard * 0.25f); // + offsetLateral; 
        // Vector3 offsetProfondeur = -directionRegard * (profondeurZone * 0.5f);
        // Vector3 offsetAvance = directionRegard * distanceDevant; 
        
        // Vector3 currentScale = zoneDepart.transform.localScale;

        zoneDepart.transform.position = cameraVR.position + (Vector3.down * 0.40f); //+ (directionRegard);//* 0.25f); //posPoitrine + offsetAvance + offsetProfondeur;
        zoneDepart.transform.rotation = Quaternion.Euler(90, cameraVR.eulerAngles.y, 0);
        //zoneDepart.transform.LookAt(directionRegard);
        // zoneDepart.transform.localScale = new Vector3(currentScale.x, profondeurZone / 2.0f, currentScale.z); 
    }

    // --- LOGIQUE METIER ---
    public void FinEssai(string stimulus, string mainAttendue, string actionJoueur, string resultat, float trt)
    {
        enregistrementEnCours = false;
        if (actionJoueur == "MAIN_DROITE") 
        {
            tagMainQuiDoitRevenir = "MainDroite";
        }
        else if (actionJoueur == "MAIN_GAUCHE") 
        {
            tagMainQuiDoitRevenir = "MainGauche";
        }
        else 
        {
            tagMainQuiDoitRevenir = "AUCUNE"; 
        }

        string modeStr = modeReaching ? "REACHING" : "GACHETTE";
        string blocStr = estEnFamiliarisation ? "FAMILIARISATION" : "MESURE";
        int phaseNum = (int)phaseActuelle + 1; 

        int echecIndiv = 0;
        if (!resultat.Contains("SUCCES")) { compteurEchecsPhase++; echecIndiv = 1; }
        
        string errPartielleEssai = "NON";
        if (erreurPartielleCeTour) { compteurErreursPartiellesPhase++; errPartielleEssai = "OUI"; }

        string trtFormat = trt.ToString("F4", CultureInfo.InvariantCulture);
        string ligneRes = $"{idSujet};{niveau};{modeStr};{phaseNum};{phaseActuelle};{blocStr};{compteurEssaiPhase};{stimulus};{mainAttendue};{actionJoueur};{resultat};{errPartielleEssai};{echecIndiv};{trtFormat}\n";
        File.AppendAllText(cheminFichierResultats, ligneRes);

        if (modeReaching && bufferCinematique.Count > 0)
        {
            File.AppendAllLines(cheminFichierKinematics, bufferCinematique);
            bufferCinematique.Clear(); 
        }

        compteurEssaiPhase++;
        if (estEnFamiliarisation)
        {
            // TODO : A quoi ça sert ?
            if (compteurEssaiPhase >= nbEssaisFamil)
            {
                estEnFamiliarisation = false; 
                compteurEssaiPhase = 0; 
                compteurSerie = 0; 
                compteurEchecsPhase = 0; compteurErreursPartiellesPhase = 0; 
                StartCoroutine(RoutineMessageEtLancement("FIN ENTRAINEMENT\n\nÇa compte pour de vrai !", 4.0f)); return;
            }
        }
        else 
        {
            compteurSerie++;
            if (compteurEssaiPhase >= nbEssaisMesure) { TerminerPhase(); return; }
            if (compteurSerie >= tailleVague) { compteurSerie = 0; StartCoroutine(RoutineDecompteVague("PAUSE", dureePauseVague)); return; }
        }
        GererSuiteEssai();
    }

    void GererSuiteEssai()
    {
        if (modeReaching && tagMainQuiDoitRevenir != "AUCUNE") 
        { 
            delaiMinCurrent = 1.0f; delaiMaxCurrent = 3.0f; 
            StartCoroutine(RoutineAttenteSecurisee());
        }
        else 
        { 
            delaiMinCurrent = 2.0f; delaiMaxCurrent = 6.0f; 
            StartCoroutine(RoutineEssai()); 
        }
    }

    IEnumerator RoutineAttenteSecurisee()
    {
        if(zoneDepart != null) zoneDepart.SetActive(false);
        enAttenteDeRetourMains = false;
        yield return new WaitForSeconds(1.0f);
        AttendreRetourPosition();
    }

    void AttendreRetourPosition()
    {
        enAttenteDeRetourMains = true;
        if(afficheurTexte != null) afficheurTexte.text = "Revenez en position\nde départ";
        if(zoneDepart != null) zoneDepart.SetActive(true); 
    }

    public void JoueurEstRevenu(string tagMainDetectee)
    {
        if (!enAttenteDeRetourMains) return;
        bool estBonneMain = (tagMainDetectee == tagMainQuiDoitRevenir);
        bool estDebutPhase = (tagMainQuiDoitRevenir == "TOUTES");

        if (estBonneMain || estDebutPhase)
        {
            enAttenteDeRetourMains = false; 
            if(zoneDepart != null) zoneDepart.SetActive(false); 
            StartCoroutine(RoutineEssai());
        }
    }

    void LancerPhase(TypePhase nouvellePhase)
    {
        phaseActuelle = nouvellePhase;
        estEnFamiliarisation = true;
        compteurEssaiPhase = 0; 
        compteurSerie = 0; 
        compteurEchecsPhase = 0; 
        compteurErreursPartiellesPhase = 0;
        tagMainQuiDoitRevenir = "TOUTES";

        string titre = ""; string instruction = "";
        if (nouvellePhase == TypePhase.Phase1_MainDroite) { titre = "PHASE 1 : MAIN DROITE"; instruction = "Utilisez uniquement la\nMAIN DROITE (Jaune)"; }
        else if (nouvellePhase == TypePhase.Phase2_MainGauche) { titre = "PHASE 2 : MAIN GAUCHE"; instruction = "Utilisez uniquement la\nMAIN GAUCHE (Bleu)"; }
        else { titre = "PHASE 3 : DISCRIMINATION"; instruction = "JAUNE = Main Droite\nBLEU = Main Gauche\nROUGE = NE PAS BOUGER"; }

        StartCoroutine(RoutineMessageEtLancement(titre + "\n\n" + instruction + "\n\n(Entrainement)", 5.0f));
    }

    void TerminerPhase()
    {
        if (phaseActuelle == TypePhase.Phase1_MainDroite) { phaseSuivante = TypePhase.Phase2_MainGauche; StartCoroutine(RoutineDecompteEtBlocage("FIN PHASE 1\nRepos", dureePauseBloc, "PHASE 2 (Main Gauche)")); }
        else if (phaseActuelle == TypePhase.Phase2_MainGauche) { phaseSuivante = TypePhase.Phase3_Decision; StartCoroutine(RoutineDecompteEtBlocage("FIN PHASE 2\nRepos", dureePauseBloc, "PHASE 3 (Discrimination)")); }
        else { if(afficheurTexte != null) afficheurTexte.text = "EXPERIENCE TERMINÉE\nMerci !"; }
    }

    IEnumerator RoutineEssai()
    {
        if (currentStimulus != null) Destroy(currentStimulus);
        if(croixFixation != null) croixFixation.SetActive(true);
        if(afficheurTexte != null) afficheurTexte.text = "";
        
        yield return new WaitForSeconds(UnityEngine.Random.Range(delaiMinCurrent, delaiMaxCurrent));
        
        if(croixFixation != null) croixFixation.SetActive(false);
        SpawnStimulus();
    }

    IEnumerator RoutineMessageEtLancement(string msg, float duree)
    {
        if(afficheurTexte != null) afficheurTexte.text = msg;
        yield return new WaitForSeconds(duree);
        if(afficheurTexte != null) afficheurTexte.text = "";
        GererSuiteEssai(); 
    }

    IEnumerator RoutineDecompteVague(string titre, float dureeTotale)
    {
        float tempsRestant = dureeTotale;
        while (tempsRestant > 0)
        {
            if(afficheurTexte != null) afficheurTexte.text = $"{titre}\nReprise dans : {tempsRestant:F0} s";
            yield return new WaitForSeconds(1.0f);
            tempsRestant -= 1.0f;
        }
        if(afficheurTexte != null) afficheurTexte.text = "";
        GererSuiteEssai();
    }

    IEnumerator RoutineDecompteEtBlocage(string titre, float dureeTotale, string nomProchainePhase)
    {
        float tempsRestant = dureeTotale;
        while (tempsRestant > 0)
        {
            if(afficheurTexte != null) afficheurTexte.text = $"{titre}\nChangement de phase dans : {tempsRestant:F0} s";
            yield return new WaitForSeconds(1.0f);
            tempsRestant -= 1.0f;
        }
        string messageAction = modeReaching ? "pour CALIBRER et LANCER" : "pour LANCER";
        if(afficheurTexte != null) afficheurTexte.text = $"Repos terminé.\n\nPrêt pour : {nomProchainePhase} ?\nAppuyez sur ENTREE {messageAction}.";
        enAttenteDeDemarrage = true;
    }

    void PreparerFichiersCSV() {
        string bureau = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        string dossierRacine = Path.Combine(bureau, "RESULTATS_EXPERIENCE_VR");
        string dossierSujet = Path.Combine(dossierRacine, idSujet);
        string dossierFinal = Path.Combine(dossierSujet, "FichierExpe");
        if (!Directory.Exists(dossierFinal)) Directory.CreateDirectory(dossierFinal);
        cheminFichierResultats = Path.Combine(dossierFinal, $"{idSujet}_RESULTATS.csv");
        cheminFichierKinematics = Path.Combine(dossierFinal, $"{idSujet}_KINEMATICS.csv");
        if (!File.Exists(cheminFichierResultats)) { File.AppendAllText(cheminFichierResultats, "Sujet_ID;NIVEAU;Mode_Exp;Phase_Num;Nom_Phase;Bloc_Type;Essai_Num;Couleur_Stimulus;Main_Attendue;Reponse_Sujet;Resultat;Err_Partielle_Indiv;Echec_Indiv;Temps_Total_Contact_sec\n"); }
        if (!File.Exists(cheminFichierKinematics)) { File.AppendAllText(cheminFichierKinematics, "Essai_Global_Num;Phase;timestamp;MainG_X;MainG_Y;MainG_Z;MainD_X;MainD_Y;MainD_Z\n"); }
    }

    void EnregistrerFrameCinematique() {
        Vector3 posG = Vector3.zero; Vector3 posD = Vector3.zero;
        if (objMainGauche != null) posG = objMainGauche.transform.position;
        if (objMainDroite != null) posD = objMainDroite.transform.position;
        float tempsActuel = Time.time - tempsDebutEssai; 
        string phaseStr = phaseActuelle.ToString();
        string ligne = $"{compteurEssaiPhase};{phaseStr};{tempsActuel.ToString("F4", CultureInfo.InvariantCulture)};{posG.x:F4};{posG.y:F4};{posG.z:F4};{posD.x:F4};{posD.y:F4};{posD.z:F4}";
        bufferCinematique.Add(ligne);
    }

    void SurveillerErreurPartielle() {
        if (erreurPartielleCeTour) return;
        float distG = 0f; float distD = 0f;
        if (objMainGauche != null) distG = Vector3.Distance(objMainGauche.transform.position, posDepartMainG);
        if (objMainDroite != null) distD = Vector3.Distance(objMainDroite.transform.position, posDepartMainD);
        if (mainQuiNeDoitPasBouger == "GAUCHE" && distG > seuilDetectionMouvement) erreurPartielleCeTour = true;
        else if (mainQuiNeDoitPasBouger == "DROITE" && distD > seuilDetectionMouvement) erreurPartielleCeTour = true;
        else if (mainQuiNeDoitPasBouger == "LES_DEUX" && (distG > seuilDetectionMouvement || distD > seuilDetectionMouvement)) erreurPartielleCeTour = true;
    }
}