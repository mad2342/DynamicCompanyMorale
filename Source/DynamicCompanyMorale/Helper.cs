﻿using Newtonsoft.Json;
using System;
using System.IO;



// Not needed atm. Keeping for reference.
namespace DynamicCompanyMorale
{
    public class SaveFields
    {
        public int ExpenseLevel = 0;

        public SaveFields(int expenseLevel)
        {
            ExpenseLevel = expenseLevel;
        }
    }

    public class Helper
    {
        public static void SaveState(string instanceGUID, DateTime saveTime)
        {
            try
            {
                int unixTimestamp = (int)(saveTime.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
                string baseDirectory = $"{ DynamicCompanyMorale.ModDirectory}";
                string filePath = baseDirectory + $"/SaveState/" + instanceGUID + "-" + unixTimestamp + ".json";
                (new FileInfo(filePath)).Directory.Create();
                using (StreamWriter writer = new StreamWriter(filePath, true))
                {
                    SaveFields fields = new SaveFields(Fields.ExpenseLevel);
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
                string baseDirectory = $"{ DynamicCompanyMorale.ModDirectory}";
                string filePath = baseDirectory + $"/SaveState/" + instanceGUID + "-" + unixTimestamp + ".json";
                if (File.Exists(filePath))
                {
                    using (StreamReader r = new StreamReader(filePath))
                    {
                        string json = r.ReadToEnd();
                        SaveFields save = JsonConvert.DeserializeObject<SaveFields>(json);
                        Fields.ExpenseLevel = save.ExpenseLevel;
                        Fields.FixExpenseLevel = true;
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