using ICities;
using System.Collections.Generic;

namespace EnhancedGarbageTruckAI
{
    public class Loader : LoadingExtensionBase
    {
        public static List<RedirectCallsState> m_redirectionStates = new List<RedirectCallsState>();

        Helper _helper;

        public override void OnCreated(ILoading loading)
        {
            _helper = Helper.Instance;

            _helper.GameLoaded = loading.loadingComplete;
        }

        public override void OnLevelLoaded(LoadMode mode)
        {
            base.OnLevelLoaded(mode);
            if (mode == LoadMode.NewGame || mode == LoadMode.LoadGame)
            {
                _helper.GameLoaded = true;
            }
        }

        public override void OnLevelUnloading()
        {
            base.OnLevelUnloading();
            _helper.GameLoaded = false;
            foreach (RedirectCallsState rcs in m_redirectionStates)
            {
                RedirectionHelper.RevertRedirect(rcs);
            }
            m_redirectionStates.Clear();
        }
    }
}