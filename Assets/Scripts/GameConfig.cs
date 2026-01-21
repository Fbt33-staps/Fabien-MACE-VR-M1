using System;

[Serializable]
public class GameConfig
{
    public string idSujet = "SUJET_DEFAUT";
    public string niveau = "NIVEAU_1"; // <--- REMPLACE "GROUPE"

    public float distanceStimulus = 0.3f;
    public float tempsPauseVague = 15.0f;
    public float tempsPauseBloc = 60.0f;
    public int nbEssaisMesure = 36;
}