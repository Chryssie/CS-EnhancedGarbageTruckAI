using ColossalFramework;
using ICities;
using System.IO;

namespace EnhancedGarbageTruckAI
{
    public class Identity : IUserMod
    {
        public string Name
        {
            get { return Settings.Instance.Tag; }
        }

        public string Description
        {
            get { return "Oversees trash services to ensure that garbage trucks are dispatched in an efficient manner."; }
        }

        public const string SETTINGFILENAME = "EnhancedGarbageTruckAI.xml";

        public static string configPath;

        public static ModConfiguration ModConf;

        public void OnSettingsUI(UIHelperBase helper)
        {
            this.InitConfigFile();
            UIHelperBase group = helper.AddGroup(Translation.GetString("Settings"));
            group.AddCheckbox(Translation.GetString("PrioritizeTargetsWithRedSigns"), ModConf.PrioritizeTargetWithRedSigns, delegate (bool isChecked)
            {
                Identity.ModConf.PrioritizeTargetWithRedSigns = isChecked;
                ModConfiguration.Serialize(Identity.configPath, Identity.ModConf);
            });
        }

        private void InitConfigFile()
        {
            try
            {
                string pathName = GameSettings.FindSettingsFileByName("gameSettings").pathName;
                string str = "";
                if (pathName != "")
                {
                    str = Path.GetDirectoryName(pathName) + Path.DirectorySeparatorChar;
                }
                Identity.configPath = str + SETTINGFILENAME;
                Identity.ModConf = ModConfiguration.Deserialize(Identity.configPath);
                if (Identity.ModConf == null)
                {
                    Identity.ModConf = ModConfiguration.Deserialize(SETTINGFILENAME);
                    if (Identity.ModConf != null && ModConfiguration.Serialize(str + SETTINGFILENAME, Identity.ModConf))
                    {
                        try
                        {
                            File.Delete(SETTINGFILENAME);
                        }
                        catch
                        {
                        }
                    }
                }
                if (Identity.ModConf == null)
                {
                    Identity.ModConf = new ModConfiguration();
                    if (!ModConfiguration.Serialize(Identity.configPath, Identity.ModConf))
                    {
                        Identity.configPath = SETTINGFILENAME;
                        ModConfiguration.Serialize(Identity.configPath, Identity.ModConf);
                    }
                }
            }
            catch
            {
            }
        }
    }
}