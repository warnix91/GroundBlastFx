using System;

namespace GroundBlastFx.Core
{
    /// <summary>
    /// Traces au sol gardées dans la sauvegarde (1.7) : noeud du scénario GroundBlastFxScenario dans la partie (persistent.sfs).
    /// Chargé dans les scènes où l'on peut revenir voir un site (vol, centre spatial, station de suivi) ;
    /// ailleurs, KSP conserve le noeud tel quel. Désactivable par le réglage « persistentMarks ».
    /// </summary>
    [KSPScenario(ScenarioCreationOptions.AddToAllGames, GameScenes.FLIGHT, GameScenes.SPACECENTER, GameScenes.TRACKSTATION)]
    public sealed class GroundBlastFxScenario : ScenarioModule
    {
        public override void OnSave(ConfigNode node)
        {
            try { ScorchManager.SaveTo(node); }
            catch (Exception e) { GeLog.ExceptionOnce("GroundBlastFxScenario.OnSave", e); }
        }

        public override void OnLoad(ConfigNode node)
        {
            try { ScorchManager.LoadFrom(node); }
            catch (Exception e) { GeLog.ExceptionOnce("GroundBlastFxScenario.OnLoad", e); }
        }
    }
}
