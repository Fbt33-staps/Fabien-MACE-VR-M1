using UnityEngine;
using UnityEngine.XR;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System;
using System.Text;

public class StimulusController : MonoBehaviour
{
    [Header("Interface Visuelle")]
    public TextMesh afficheurTexte;
    public GameObject croixFixation;
    public GameObject zoneDepart;

    // --- CONFIGURATION ---
    private GameConfig maConfig;

    [Header("Paramètres Sujet")]
    public string idSujet = "SUJET_01";
    public string niveau = "NIVEAU_1";

    [Tooltip("Cocher pour le mode Mouvement. Décocher pour Gachette.")]
    public bool modeReaching = false;

    // --- PROTOCOLE ---
    public enum TypePhase { Phase1_MainDroite, Phase2_MainGauche, Phase3_Decision }

    [Header("État (Lecture Seule)")]
    public TypePhase phaseActuelle;
    private TypePhase phaseSuivante;

    public bool estEnFamiliarisation = true;
    public int compteurEssaiPhase = 0;
    public int compteurSerie = 0;

    // COMPTEURS
    public int compteurEchecsPhase = 0;
    public int compteurErreursPartiellesPhase = 0;

    private bool enAttenteDeDemarrage = false;
    private bool enAttenteDeRetourMains = false;

    // --- CINEMATIQUE ---
    private bool enregistrementEnCours = false;
    private List<string> bufferCinematique = new List<string>();
    private float tempsDebutEssai;

    // --- DETECTION ERREUR PARTIELLE ---
    [Header("Paramètres Détection")]
    public float seuilDetectionMouvement = 0.05f; // 5 cm
    private Vector3 posDepartMainG;
    private Vector3 posDepartMainD;
    private bool erreurPartielleCeTour = false;
    private string mainQuiNeDoitPasBouger = "";

    private GameObject objMainGauche;
    private GameObject objMainDroite;

    // --- VOLUMETRIE ---
    private int nbEssaisFamil = 6;
    public int nbEssaisMesure = 36;
    private int tailleVague = 12;

    [Header("Temps de Pause")]
    public float dureePauseVague = 15.0f;
    public float dureePauseBloc = 60.0f;

    private float delaiMinCurrent;
    private float delaiMaxCurrent;

    // --- MATERIEL ---
    [Header("Références Unity")]
    public GameObject stimulusPrefab;
    public Transform cameraVR;
    public float distance = 0.3f;

    [Header("Couleurs")]
    public Material matJaune;
    public Material matBleu;
    public Material matRouge;

    private string cheminFichierResultats;
    private string cheminFichierKinematics;

    void Start()
    {
        GererConfigurationPrioritaire();

        // Création de la structure de dossiers pour Python
        PreparerFichiersCSV();

        // Récupération des objets Mains
        objMainGauche = GameObject.FindGameObjectWithTag("MainGauche");
        objMainDroite = GameObject.FindGameObjectWithTag("MainDroite");

        if (croixFixation != null) croixFixation.SetActive(false);
        if (zoneDepart != null) zoneDepart.SetActive(false);

        phaseSuivante = TypePhase.Phase1_MainDroite;
        enAttenteDeDemarrage = true;

        string modeTexte = modeReaching ? "REACHING (Mouvement)" : "GACHETTE (Statique)";
        string msgAccueil = $"ID : {idSujet} | Niv : {niveau}\nMode : {modeTexte}\n\nAppuyez sur ENTREE pour COMMENCER";

        if (afficheurTexte != null) afficheurTexte.text = msgAccueil;
    }

    void Update()
    {
        // GESTION DEMARRAGE (Touche ENTREE)
        if (enAttenteDeDemarrage)
        {
            if (Input.GetKeyDown(KeyCode.Return))
            {
                if (modeReaching) CalibrerZoneDepart();
                enAttenteDeDemarrage = false;
                LancerPhase(phaseSuivante);
            }
        }

        // GESTION ENREGISTREMENT CONTINU
        if (enregistrementEnCours && modeReaching)
        {
            EnregistrerFrameCinematique();
            SurveillerErreurPartielle();
        }
    }

    // --- 1. CONFIGURATION & CSV (DOSSIERS) ---

    void GererConfigurationPrioritaire()
    {
        string chemin = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "config_experience.json");
        if (File.Exists(chemin))
        {
            string jsonLecture = File.ReadAllText(chemin);
            maConfig = JsonUtility.FromJson<GameConfig>(jsonLecture);

            if (!string.IsNullOrEmpty(idSujet) && idSujet != "NON_DEFINI")
            {
                maConfig.idSujet = idSujet; maConfig.niveau = niveau;
                File.WriteAllText(chemin, JsonUtility.ToJson(maConfig, true));
            }
            else
            {
                idSujet = maConfig.idSujet; niveau = maConfig.niveau;
            }
            distance = maConfig.distanceStimulus; nbEssaisMesure = maConfig.nbEssaisMesure;
        }
        else
        {
            maConfig = new GameConfig(); maConfig.idSujet = idSujet; maConfig.niveau = niveau;
            File.WriteAllText(chemin, JsonUtility.ToJson(maConfig, true));
        }
    }

    void PreparerFichiersCSV()
    {
        // STRUCTURE EXACTE POUR PYTHON
        string bureau = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        string dossierRacine = Path.Combine(bureau, "RESULTATS_EXPERIENCE_VR");
        string dossierSujet = Path.Combine(dossierRacine, idSujet);
        string dossierFinal = Path.Combine(dossierSujet, "FichierExpe");

        if (!Directory.Exists(dossierFinal)) Directory.CreateDirectory(dossierFinal);

        cheminFichierResultats = Path.Combine(dossierFinal, $"{idSujet}_RESULTATS.csv");
        cheminFichierKinematics = Path.Combine(dossierFinal, $"{idSujet}_KINEMATICS.csv");

        // En-tête RESULTATS
        if (!File.Exists(cheminFichierResultats))
        {
            string enteteRes = "Sujet_ID;NIVEAU;Mode_Exp;Phase_Num;Nom_Phase;Bloc_Type;Essai_Num;Stimulus;Reponse_Sujet;Resultat;Nb_Echecs_Phase;erreur partielle;TRT_sec\n";
            File.AppendAllText(cheminFichierResultats, enteteRes);
        }

        // En-tête KINEMATICS (Sans Tête)
        if (!File.Exists(cheminFichierKinematics))
        {
            string enteteKin = "Essai_Global_Num;Phase;Temps_Relatif_sec;MainG_X;MainG_Y;MainG_Z;MainD_X;MainD_Y;MainD_Z\n";
            File.AppendAllText(cheminFichierKinematics, enteteKin);
        }
        Debug.Log($"✅ CSV prêts dans : {dossierFinal}");
    }

    // --- 2. LOGIQUE CINEMATIQUE & ERREURS ---

    void EnregistrerFrameCinematique()
    {
        Vector3 posG = Vector3.zero;
        Vector3 posD = Vector3.zero;

        if (objMainGauche != null) posG = objMainGauche.transform.position;
        if (objMainDroite != null) posD = objMainDroite.transform.position;

        float tempsActuel = Time.time - tempsDebutEssai;
        string phaseStr = phaseActuelle.ToString();

        // Format pour Python
        string ligne = $"{compteurEssaiPhase};{phaseStr};{tempsActuel:F4};{posG.x:F4};{posG.y:F4};{posG.z:F4};{posD.x:F4};{posD.y:F4};{posD.z:F4}";
        bufferCinematique.Add(ligne);
    }

    void SurveillerErreurPartielle()
    {
        if (erreurPartielleCeTour) return;

        float distG = 0f;
        float distD = 0f;

        if (objMainGauche != null) distG = Vector3.Distance(objMainGauche.transform.position, posDepartMainG);
        if (objMainDroite != null) distD = Vector3.Distance(objMainDroite.transform.position, posDepartMainD);

        // Seuil simple (en attendant algo DeepSearch)
        if (mainQuiNeDoitPasBouger == "GAUCHE" && distG > seuilDetectionMouvement) erreurPartielleCeTour = true;
        else if (mainQuiNeDoitPasBouger == "DROITE" && distD > seuilDetectionMouvement) erreurPartielleCeTour = true;
        else if (mainQuiNeDoitPasBouger == "LES_DEUX" && (distG > seuilDetectionMouvement || distD > seuilDetectionMouvement)) erreurPartielleCeTour = true;
    }

    // --- 3. DÉROULEMENT DU JEU (SPAWN & FIN ESSAI) ---

    void SpawnStimulus()
    {
        Vector3 pos = cameraVR.position + (cameraVR.forward * distance);
        Quaternion rot = cameraVR.rotation * Quaternion.Euler(90, 0, 0);
        GameObject disque = Instantiate(stimulusPrefab, pos, rot);

        int choix = 0;
        if (phaseActuelle == TypePhase.Phase1_MainDroite) choix = 0;
        else if (phaseActuelle == TypePhase.Phase2_MainGauche) choix = 1;
        else { float h = UnityEngine.Random.Range(0f, 100f); choix = (h < 25f) ? 2 : ((UnityEngine.Random.Range(0, 2) == 0) ? 0 : 1); }

        Renderer r = disque.GetComponent<Renderer>();
        string ns = "", ma = "";

        // Init variables de surveillance
        erreurPartielleCeTour = false;
        mainQuiNeDoitPasBouger = "AUCUNE_RESTRICTION";

        if (choix == 0) { r.material = matJaune; ns = "JAUNE"; ma = "MAIN_DROITE"; mainQuiNeDoitPasBouger = "GAUCHE"; }
        else if (choix == 1) { r.material = matBleu; ns = "BLEU"; ma = "MAIN_GAUCHE"; mainQuiNeDoitPasBouger = "DROITE"; }
        else { r.material = matRouge; ns = "ROUGE"; ma = "AUCUNE"; mainQuiNeDoitPasBouger = "LES_DEUX"; }

        DisqueCollision script = disque.GetComponent<DisqueCollision>();
        if (script != null) { script.manager = this; script.modeReaching = modeReaching; script.couleurID = choix; script.nomCondition = ns; script.mainAttendue = ma; }

        // Init enregistrement (si mode mouvement)
        if (modeReaching)
        {
            bufferCinematique.Clear();
            tempsDebutEssai = Time.time;

            // Snapshot position départ
            if (objMainGauche != null) posDepartMainG = objMainGauche.transform.position;
            if (objMainDroite != null) posDepartMainD = objMainDroite.transform.position;

            enregistrementEnCours = true;
        }
    }

    public void FinEssai(string stimulus, string mainAttendue, string actionJoueur, string resultat, float trt)
    {
        enregistrementEnCours = false;

        string modeStr = modeReaching ? "REACHING" : "GACHETTE";
        string blocStr = estEnFamiliarisation ? "FAMILIARISATION" : "MESURE";
        int phaseNum = (int)phaseActuelle + 1;

        if (!resultat.Contains("SUCCES")) compteurEchecsPhase++;

        // Incrémentation compteur Erreur Partielle
        if (erreurPartielleCeTour) compteurErreursPartiellesPhase++;

        string errPartielleStr = erreurPartielleCeTour ? "OUI" : "NON";

        // Ecriture CSV RESULTATS
        string ligneRes = $"{idSujet};{niveau};{modeStr};{phaseNum};{phaseActuelle};{blocStr};{compteurEssaiPhase};{stimulus};{actionJoueur};{resultat};{compteurEchecsPhase};{compteurErreursPartiellesPhase};{trt:F4}\n";
        File.AppendAllText(cheminFichierResultats, ligneRes);

        // Ecriture CSV KINEMATICS
        if (modeReaching && bufferCinematique.Count > 0)
        {
            File.AppendAllLines(cheminFichierKinematics, bufferCinematique);
            bufferCinematique.Clear();
        }

        // Suite du protocole
        compteurEssaiPhase++;
        if (estEnFamiliarisation)
        {
            if (compteurEssaiPhase >= nbEssaisFamil)
            {
                estEnFamiliarisation = false; compteurEssaiPhase = 0; compteurSerie = 0;
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

    // --- 4. GESTION DES PAUSES & PHASES ---

    void GererSuiteEssai()
    {
        if (modeReaching) { delaiMinCurrent = 1.0f; delaiMaxCurrent = 5.0f; AttendreRetourPosition(); }
        else { delaiMinCurrent = 2.0f; delaiMaxCurrent = 6.0f; StartCoroutine(RoutineEssai()); }
    }

    void CalibrerZoneDepart()
    {
        if (zoneDepart != null && cameraVR != null)
        {
            Vector3 directionRegard = cameraVR.forward; directionRegard.y = 0; directionRegard.Normalize();
            if (directionRegard == Vector3.zero) directionRegard = Vector3.forward;
            Vector3 posPoitrine = cameraVR.position + (Vector3.down * 0.35f) + (directionRegard * 0.20f);
            zoneDepart.transform.position = posPoitrine;
        }
    }

    public void JoueurEstRevenu(string tagMain)
    {
        if (!enAttenteDeRetourMains) return;

        bool mainCorrecte = true;
        if (phaseActuelle == TypePhase.Phase1_MainDroite && tagMain != "MainDroite") mainCorrecte = false;
        if (phaseActuelle == TypePhase.Phase2_MainGauche && tagMain != "MainGauche") mainCorrecte = false;

        if (mainCorrecte)
        {
            enAttenteDeRetourMains = false;
            if (zoneDepart != null) zoneDepart.SetActive(false);
            StartCoroutine(RoutineEssai());
        }
    }

    void AttendreRetourPosition()
    {
        enAttenteDeRetourMains = true;
        if (zoneDepart != null) zoneDepart.SetActive(true);
    }

    void LancerPhase(TypePhase nouvellePhase)
    {
        phaseActuelle = nouvellePhase;
        estEnFamiliarisation = true;
        compteurEssaiPhase = 0;
        compteurSerie = 0;
        compteurEchecsPhase = 0;
        compteurErreursPartiellesPhase = 0;

        string titre = "";
        string instruction = "";

        if (nouvellePhase == TypePhase.Phase1_MainDroite) { titre = "PHASE 1 : MAIN DROITE"; instruction = "Utilisez uniquement la\nMAIN DROITE (Jaune)"; }
        else if (nouvellePhase == TypePhase.Phase2_MainGauche) { titre = "PHASE 2 : MAIN GAUCHE"; instruction = "Utilisez uniquement la\nMAIN GAUCHE (Bleu)"; }
        else { titre = "PHASE 3 : DISCRIMINATION"; instruction = "JAUNE = Main Droite\nBLEU = Main Gauche\nROUGE = NE PAS BOUGER"; }

        StartCoroutine(RoutineMessageEtLancement(titre + "\n\n" + instruction + "\n\n(Entrainement)", 5.0f));
    }

    void TerminerPhase()
    {
        if (phaseActuelle == TypePhase.Phase1_MainDroite)
        {
            phaseSuivante = TypePhase.Phase2_MainGauche;
            StartCoroutine(RoutineDecompteEtBlocage("FIN PHASE 1\nRepos", dureePauseBloc, "PHASE 2 (Main Gauche)"));
        }
        else if (phaseActuelle == TypePhase.Phase2_MainGauche)
        {
            phaseSuivante = TypePhase.Phase3_Decision;
            StartCoroutine(RoutineDecompteEtBlocage("FIN PHASE 2\nRepos", dureePauseBloc, "PHASE 3 (Discrimination)"));
        }
        else
        {
            if (afficheurTexte != null) afficheurTexte.text = "EXPERIENCE TERMINÉE\nMerci !";
        }
    }

    // --- COROUTINES (Timing) ---

    IEnumerator RoutineEssai()
    {
        if (croixFixation != null) croixFixation.SetActive(true);
        if (afficheurTexte != null) afficheurTexte.text = "";

        yield return new WaitForSeconds(UnityEngine.Random.Range(delaiMinCurrent, delaiMaxCurrent));

        if (croixFixation != null) croixFixation.SetActive(false);
        SpawnStimulus();
    }

    IEnumerator RoutineMessageEtLancement(string msg, float duree)
    {
        if (afficheurTexte != null) afficheurTexte.text = msg;
        yield return new WaitForSeconds(duree);
        if (afficheurTexte != null) afficheurTexte.text = "";
        GererSuiteEssai();
    }

    IEnumerator RoutineDecompteVague(string titre, float dureeTotale)
    {
        float tempsRestant = dureeTotale;
        while (tempsRestant > 0)
        {
            if (afficheurTexte != null) afficheurTexte.text = $"{titre}\nReprise dans : {tempsRestant:F0} s";
            yield return new WaitForSeconds(1.0f);
            tempsRestant -= 1.0f;
        }
        if (afficheurTexte != null) afficheurTexte.text = "";
        GererSuiteEssai();
    }

    IEnumerator RoutineDecompteEtBlocage(string titre, float dureeTotale, string nomProchainePhase)
    {
        float tempsRestant = dureeTotale;
        while (tempsRestant > 0)
        {
            if (afficheurTexte != null) afficheurTexte.text = $"{titre}\nChangement de phase dans : {tempsRestant:F0} s";
            yield return new WaitForSeconds(1.0f);
            tempsRestant -= 1.0f;
        }

        string messageAction = modeReaching ? "pour CALIBRER et LANCER" : "pour LANCER";

        if (afficheurTexte != null)
            afficheurTexte.text = $"Repos terminé.\n\nPrêt pour : {nomProchainePhase} ?\nAppuyez sur ENTREE {messageAction}.";

        enAttenteDeDemarrage = true;
    }
}