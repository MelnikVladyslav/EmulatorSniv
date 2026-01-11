using System.Collections;
using UnityEngine;

namespace Assets.Scripts.Menu.Settings.Localization
{
    public class LocalizationManager : MonoBehaviour
    {
        public void Ukr()
        {
            string language = "Ukr";
            PlayerPrefs.SetString("Language", language);
        }

        public void Eng()
        {
            string language = "Eng";
            PlayerPrefs.SetString("Language", language);
        }

        public void rus()
        {
            string language = "rus";
            PlayerPrefs.SetString("Language", language);
        }

        public void Sp()
        {
            string language = "Sp";
            PlayerPrefs.SetString("Language", language);
        }

        public void Fr()
        {
            string language = "Fr";
            PlayerPrefs.SetString("Language", language);
        }

        public void Hin()
        {
            string language = "Hin";
            PlayerPrefs.SetString("Language", language);
        }
        public void ch()
        {
            string language = "ch";
            PlayerPrefs.SetString("Language", language);
        }
        public void Jap()
        {
            string language = "Jap";
            PlayerPrefs.SetString("Language", language);
        }
    }
}