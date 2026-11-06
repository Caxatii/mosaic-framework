using TMPro;
using UnityEngine;

namespace Mosaic.Ui
{
    /// <summary>
    /// The view of the cheat console (the package's CheatConsole prefab): history, input line, help and suggestions.
    /// </summary>
    public sealed class CheatConsoleView : MonoBehaviour
    {
        public TMP_Text History;
        public TMP_InputField Input;
        public TMP_Text Help;
    }
}
