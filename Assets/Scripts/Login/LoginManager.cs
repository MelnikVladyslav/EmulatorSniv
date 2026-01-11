using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Collections;
using System.Security.Cryptography;
using System.Text;
using Unity.VisualScripting;
using Assets.Scripts.Menu.Settings.Localization;
using UnityEngine.SceneManagement;

public class LoginManager : MonoBehaviour
{
    public InputField emailField;
    public InputField passwordField;
    public Text resText;

    string res = "";

    private string baseUrl = "http://kyivdream.com/page/overifyhf29h293hf278";

    public void OnLoginClicked()
    {
        string email = emailField.text;
        string password = passwordField.text;

        StartCoroutine(SendLoginRequest(email, password));
    }

    private void Update()
    {
        if (res != "")
        {
            if (res == "overmax")
            {
                resText.color = Color.red;
                resText.text = "Перевищено ліміт, спробуйте завтра";
                resText.GetComponent<TextLanguage>().textUkr = "Перевищено ліміт, спробуйте завтра";
                resText.GetComponent<TextLanguage>().textrus = "";
                resText.GetComponent<TextLanguage>().textEng = "Limit exceeded, try again tomorrow";
                resText.GetComponent<TextLanguage>().textSp = "Límite excedido, inténtelo nuevamente mañana";
                resText.GetComponent<TextLanguage>().textch = "超出限制，明天再試";
                resText.GetComponent<TextLanguage>().textFr = "Limite dépassée, réessayez demain";
                resText.GetComponent<TextLanguage>().textHin = "सीमा पार हो गई, कल पुनः प्रयास करें";
                resText.GetComponent<TextLanguage>().textJap = "制限を超えました。明日もう一度お試しください";
            }
            if (res == "fail")
            {
                resText.color = Color.red;
                resText.text = "Неправильний пароль";
                resText.GetComponent<TextLanguage>().textUkr = "Неправильний пароль";
                resText.GetComponent<TextLanguage>().textrus = "";
                resText.GetComponent<TextLanguage>().textEng = "Incorrect password";
                resText.GetComponent<TextLanguage>().textSp = "Contraseña incorrecta";
                resText.GetComponent<TextLanguage>().textch = "密碼不正確";
                resText.GetComponent<TextLanguage>().textFr = "Mot de passe incorrect";
                resText.GetComponent<TextLanguage>().textHin = "गलत पासवर्ड";
                resText.GetComponent<TextLanguage>().textJap = "パスワードが間違っています";
            }
            if (res == "no_email")
            {
                resText.color = Color.red;
                resText.text = "Неправильний логін";
                resText.GetComponent<TextLanguage>().textUkr = "Неправильний логін";
                resText.GetComponent<TextLanguage>().textrus = "";
                resText.GetComponent<TextLanguage>().textEng = "Incorrect login";
                resText.GetComponent<TextLanguage>().textSp = "Inicio de sesión incorrecto";
                resText.GetComponent<TextLanguage>().textch = "登入資訊不正確";
                resText.GetComponent<TextLanguage>().textFr = "Connexion incorrecte";
                resText.GetComponent<TextLanguage>().textHin = "ग़लत लॉग इन";
                resText.GetComponent<TextLanguage>().textJap = "ログインが間違っています";
            }
            if (res == "ok")
            {
                resText.text = "";
                resText.GetComponent<TextLanguage>().textUkr = "";
                resText.GetComponent<TextLanguage>().textrus = "";
                resText.GetComponent<TextLanguage>().textEng = "";
                resText.GetComponent<TextLanguage>().textSp = "";
                resText.GetComponent<TextLanguage>().textch = "";
                resText.GetComponent<TextLanguage>().textFr = "";
                resText.GetComponent<TextLanguage>().textHin = "";
                resText.GetComponent<TextLanguage>().textJap = "";
            }
        }        
    }

    IEnumerator SendLoginRequest(string email, string password)
    {
        string url = $"{baseUrl}?email={UnityWebRequest.EscapeURL(email)}&pass={password}";
        UnityWebRequest request = UnityWebRequest.Get(url);

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            res = request.downloadHandler.text;

            if (res == "ok")
            {
                SceneManager.LoadSceneAsync(1);
            }
        }
        else
        {
            Debug.Log($"Помилка: {request.error}");
        }
    }
}