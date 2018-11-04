using Newtonsoft.Json;
using System;
using System.IO;



namespace DynamicCompanyMorale
{
    public class SaveFields
    {
        public int EventMoraleModifier = 0;

        public SaveFields(int eventMoraleModifier)
        {
            EventMoraleModifier = eventMoraleModifier;
        }
    }

    public class Helper
    {
        public static void SaveState(string instanceGUID, DateTime saveTime)
        {
            try
            {
                int unixTimestamp = (int)(saveTime.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
                //string baseDirectory = Directory.GetParent(Directory.GetParent($"{ DynamicCompanyMorale.ModDirectory}").FullName).FullName;
                string baseDirectory = $"{ DynamicCompanyMorale.ModDirectory}";
                string filePath = baseDirectory + $"/SaveState/" + instanceGUID + "-" + unixTimestamp + ".json";
                (new FileInfo(filePath)).Directory.Create();
                using (StreamWriter writer = new StreamWriter(filePath, true))
                {
                    SaveFields fields = new SaveFields(Fields.EventMoraleModifier);
                    string json = JsonConvert.SerializeObject(fields);
                    writer.Write(json);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
            }
        }

        public static void LoadState(string instanceGUID, DateTime saveTime)
        {
            try
            {
                int unixTimestamp = (int)(saveTime.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
                //string baseDirectory = Directory.GetParent(Directory.GetParent($"{ DynamicCompanyMorale.ModDirectory}").FullName).FullName;
                string baseDirectory = $"{ DynamicCompanyMorale.ModDirectory}";
                string filePath = baseDirectory + $"/SaveState/" + instanceGUID + "-" + unixTimestamp + ".json";
                if (File.Exists(filePath))
                {
                    using (StreamReader r = new StreamReader(filePath))
                    {
                        string json = r.ReadToEnd();
                        SaveFields save = JsonConvert.DeserializeObject<SaveFields>(json);
                        Fields.EventMoraleModifier = save.EventMoraleModifier;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
            }
        }
    }
}