#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace Crosstales.Common.EditorTask
{
   /// <summary>Checks if a 'Happy new year'-message must be displayed.</summary>
   [InitializeOnLoad]
   public static class NYCheck
   {
      private const string KEY_NYCHECK_DATE = "CT_CFG_NYCHECK_DATE";
      private static readonly System.Random rnd = new System.Random();

      #region Constructor

      static NYCheck()
      {
         string lastYear = EditorPrefs.GetString(KEY_NYCHECK_DATE);

         string year = System.DateTime.Now.ToString("yyyy");
         //string year = "9999"; //only for test

         string month = System.DateTime.Now.ToString("MM");
         //string month = "01"; //only for test

         if (!year.Equals(lastYear) && month.Equals("01"))
         {
            Debug.LogWarning(createString("-", 400));
            Debug.LogWarning($"<color=yellow>¸.•°*”˜˜”*°•.¸ ★</color>  <b><color=darkblue>crosstales LLC</color></b> wishes you a <b>happy</b> and <b>successful <color=orange>{year}</color></b>!  <color=yellow>★ ¸.•*¨`*•.</color><color=cyan>♫</color><color=red>❤</color><color=lime>♫</color><color=red>❤</color><color=magenta>♫</color><color=red>❤</color>");
            Debug.LogWarning(createString("-", 400));

            if (!year.Equals("9999"))
               EditorPrefs.SetString(KEY_NYCHECK_DATE, year);
         }
      }

      private static string createString(string replaceChars, int stringLength)
      {
         if (replaceChars != null)
         {
            if (replaceChars.Length > 1)
            {
               char[] chars = new char[stringLength];

               for (int ii = 0; ii < stringLength; ii++)
               {
                  chars[ii] = replaceChars[rnd.Next(0, replaceChars.Length)];
               }

               return new string(chars);
            }

            return replaceChars.Length == 1 ? new string(replaceChars[0], stringLength) : string.Empty;
         }

         return string.Empty;
      }
      #endregion
   }
}
#endif
// © 2017-2023 crosstales LLC (https://www.crosstales.com)