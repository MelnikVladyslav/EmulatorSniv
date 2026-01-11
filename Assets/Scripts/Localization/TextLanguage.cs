using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Assets.Scripts.Menu.Settings.Localization
{
    public class TextLanguage : MonoBehaviour
    {
        public string language;
        Text text;

        public string textUkr;
        public string textEng;
        public string textrus;
        public string textSp;
        public string textFr;
        public string textHin;
        public string textch;
        public string textJap;

        void Start()
        {
            text = GetComponent<Text>();
        }

        // Update is called once per frame
        void Update()
        {
            language = PlayerPrefs.GetString("Language");

            if (language == "" || language == "Ukr")
            {
                text.text = textUkr;
            }
            else if (language == "Eng")
            {
                text.text = textEng;
            }
            else if (language == "rus")
            {
                text.text = textrus;
            }
            else if (language == "Sp")
            {
                text.text = textSp;
            }
            else if (language == "Fr")
            {
                text.text = textFr;
            }
            else if (language == "Hin")
            {
                text.text = textHin;
            }
            else if (language == "ch")
            {
                text.text = textch;
            }
            else if (language == "Jap")
            {
                text.text = textJap;
            }
        }
    }
}