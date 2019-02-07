using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class EditorFindEditorTools : EditorWindow, IHasCustomMenu
{
    // Things you probably want to modify to your personal preferences

    private static readonly List<string> m_lSkipRoots = new List<string> { "CONTEXT", "&File", "&Help", "Help", "Component", "&Window" };
    private const string c_sToolPath = "Help/Find Editor Tools %t";
    private const int c_iToolPriority = 2000;

    //

    private const string c_sEditorPrefsPath = "phwitti.unity.find_editor_tools";

    private bool m_bInit = false;
    private int m_iSelected = 0;
    private float m_fScrollBar = 0.0f;
    private string m_sSearchString = System.String.Empty;

    private int m_iMenuCommandsFilteredCount = 0;
    private List<string> m_lMenuCommands = new List<string>();
    private List<string> m_lMenuCommandsFiltered = new List<string>();
    private List<ToolUsage> m_lMenuCommandsUsage = new List<ToolUsage>();
    private System.Text.StringBuilder m_stringBuilder = new System.Text.StringBuilder();

    private static readonly string[] c_arDeserializeSplitEntries = new string[] { ";;" };
    private static readonly string[] c_arDeserializeSplitEntry = new string[] { ",," };

    private const float c_fButtonStartPosition = 24.0f;
    private const float c_fButtonHeight = 32.0f;
    private const float c_fSrollbarWidth = 15.0f;
    private const float c_fFavoriteButtonWidth = 20.0f;
    private const string c_sFavoriteStarFilled = " \u2605";
    private static readonly Color c_cHover = new Color32(95, 140, 226, 255);
    private static readonly Color c_cDefault = new Color32(32, 32, 32, 255);
    private static readonly Color c_cFavoriteText = new Color32(245, 150, 5, 255);
    private static readonly Color c_cNoFavoriteText = new Color32(92, 92, 92, 255);
    private static readonly Color c_cFavoriteBackground = new Color32(150, 75, 5, 255);
    private static readonly Color c_cNoFavoriteBackground = new Color32(16, 16, 16, 255);
    private GUIStyle m_guiStyleHover = new GUIStyle();
    private GUIStyle m_guiStyleDefault = new GUIStyle();
    private GUIStyle m_guiStyleFavorite = new GUIStyle();
    private GUIStyle m_guiStyleNoFavorite = new GUIStyle();

    //

    [MenuItem(c_sToolPath, false, c_iToolPriority)]
    public static void FindTool()
    {
        EditorFindEditorTools window = EditorWindow.GetWindow<EditorFindEditorTools>();
        window.titleContent = new GUIContent("Find Editor Tools");
        window.autoRepaintOnSceneChange = false;
        window.wantsMouseMove = false;
        window.minSize = new Vector2(374f, 100f);
        window.Init();
        window.Show();
    }

    //

    private void OnEnable()
    {
        this.Init();
        this.Repaint();
    }

    private void OnGUI()
    {
        EditorGUI.BeginChangeCheck();
        {
            EditorGUILayout.BeginHorizontal();
            {
                const string sFindSearchFieldControlName = "FindEditorToolsSearchField";

                GUI.SetNextControlName(sFindSearchFieldControlName);
                m_sSearchString = EditorGUILayout.TextField(m_sSearchString, GUI.skin.FindStyle("ToolbarSeachTextField"));
                if (GUILayout.Button("", GUI.skin.FindStyle("ToolbarSeachCancelButton")))
                {
                    m_sSearchString = "";
                }
                if (!m_bInit)
                {
                    EditorGUI.FocusTextInControl(sFindSearchFieldControlName);
                    m_bInit = true;
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        if (EditorGUI.EndChangeCheck())
        {
            this.RecreateFilteredList();
            m_iSelected = Mathf.Clamp(m_iSelected, 0, m_iMenuCommandsFilteredCount - 1);
        }

        //

        Rect rectPosition = this.position;
        float fButtonAreaHeight = rectPosition.height - c_fButtonStartPosition;
        float fButtonCount = fButtonAreaHeight / c_fButtonHeight;
        int iButtonCountFloor = Mathf.Min(Mathf.FloorToInt(fButtonCount), m_iMenuCommandsFilteredCount); // used for showing the scrollbar
        int iButtonCountCeil = Mathf.Min(Mathf.CeilToInt(fButtonCount), m_iMenuCommandsFilteredCount);   // used to additionally show the last button (even if visible only in half)
        bool bScrollbar = iButtonCountCeil < m_iMenuCommandsFilteredCount;

        //

        if (bScrollbar)
        {
            m_fScrollBar = GUI.VerticalScrollbar(
                new Rect(
                    rectPosition.width - c_fSrollbarWidth,
                    c_fButtonStartPosition,
                    c_fSrollbarWidth,
                    rectPosition.height - c_fButtonStartPosition
                ),
                m_fScrollBar,
                iButtonCountFloor * c_fButtonHeight,
                0.0f,
                m_iMenuCommandsFilteredCount * c_fButtonHeight
            );
        }

        int iBase = bScrollbar ? Mathf.RoundToInt(m_fScrollBar / c_fButtonHeight) : 0;
        for (int i = iBase, j = 0, imax = Mathf.Min(iBase + iButtonCountCeil, m_iMenuCommandsFilteredCount); i < imax; i++, j++)
        {
            string sCommand = m_lMenuCommandsFiltered[i];
            bool bSelected = (i == m_iSelected);

            Rect rect = new Rect(
                c_fFavoriteButtonWidth, 
                c_fButtonStartPosition + j * c_fButtonHeight,
                rectPosition.width - (bScrollbar ? c_fSrollbarWidth : 0.0f) - c_fFavoriteButtonWidth,
                c_fButtonHeight - 1.0f
            );
            EditorGUI.DrawRect(rect, bSelected ? c_cHover : c_cDefault);
            EditorGUI.LabelField(rect, sCommand, bSelected ? m_guiStyleHover : m_guiStyleDefault);

            bool bFavorite = this.IsFavorite(sCommand);
            Rect rectFavorite = new Rect(0.0f, c_fButtonStartPosition + j * c_fButtonHeight, c_fFavoriteButtonWidth, c_fButtonHeight - 1.0f);
            EditorGUI.DrawRect(rectFavorite, bFavorite ? c_cFavoriteBackground : c_cNoFavoriteBackground);
            EditorGUI.LabelField(rectFavorite, c_sFavoriteStarFilled, bFavorite ? m_guiStyleFavorite : m_guiStyleNoFavorite);
        }

        //

        this.UpdateEvent(Event.current, iBase, bScrollbar);
    }

    //

    // Main Operations

    private void Init()
    {
        this.Load();
        this.RecreateCommandList();

        m_guiStyleDefault.alignment = TextAnchor.MiddleCenter;
        m_guiStyleDefault.normal.textColor = Color.grey;

        m_guiStyleHover.alignment = TextAnchor.MiddleCenter;
        m_guiStyleHover.normal.textColor = Color.black;

        m_guiStyleFavorite.alignment = TextAnchor.MiddleLeft;
        m_guiStyleFavorite.normal.textColor = c_cFavoriteText;
        m_guiStyleFavorite.fontSize = 13;

        m_guiStyleNoFavorite.alignment = TextAnchor.MiddleLeft;
        m_guiStyleNoFavorite.normal.textColor = c_cNoFavoriteText;
        m_guiStyleNoFavorite.fontSize = 13;

        m_bInit = false;
    }

    private void CheckScrollToSelected()
    {
        float fButtonAreaHeight = this.position.height - c_fButtonStartPosition;
        float fButtonCount = fButtonAreaHeight / c_fButtonHeight;
        int iBase = Mathf.RoundToInt(m_fScrollBar / c_fButtonHeight);
        int iButtonCountFloor = Mathf.Min(Mathf.FloorToInt(fButtonCount), m_iMenuCommandsFilteredCount);

        if (m_iSelected >= Mathf.Min(iBase + iButtonCountFloor, m_iMenuCommandsFilteredCount))
        {
            m_fScrollBar = c_fButtonHeight * (m_iSelected - iButtonCountFloor + 1);
        }
        else if (m_iSelected < iBase)
        {
            m_fScrollBar = c_fButtonHeight * m_iSelected;
        }
    }

    private void ExecuteCommand(string _sCommand)
    {
        int iIndex = -1;
        if ((iIndex = m_lMenuCommandsUsage.FindIndex(x => x.Path == _sCommand)) == -1)
        {
            m_lMenuCommandsUsage.Add(new ToolUsage(_sCommand, 1, false));
        }
        else
        {
            m_lMenuCommandsUsage[iIndex].Count++;
        }

        EditorApplication.ExecuteMenuItem(_sCommand);

        this.Save();
        this.Close();
    }

    private void UpdateEvent(Event _event, int _iCurrentBase, bool _bScrollbar)
    {
        if (_event == null)
            return;

        switch (_event.type)
        {
            case EventType.MouseDown:

                int iIndex = _iCurrentBase + Mathf.FloorToInt((_event.mousePosition.y - c_fButtonStartPosition) / c_fButtonHeight);
                if (iIndex >= 0 && iIndex < m_iMenuCommandsFilteredCount && (!_bScrollbar || _event.mousePosition.x < this.position.width - c_fSrollbarWidth))
                {
                    if (Event.current.mousePosition.x > c_fFavoriteButtonWidth)
                    {
                        this.ExecuteCommand(m_lMenuCommandsFiltered[iIndex]);
                    }
                    else
                    {
                        this.ToggleFavorite(m_lMenuCommandsFiltered[iIndex]);
                    }
                }

                break;

            case EventType.KeyUp:

                switch (_event.keyCode)
                {
                    case KeyCode.Return:
                        if (m_iSelected < m_iMenuCommandsFilteredCount)
                        {
                            this.ExecuteCommand(m_lMenuCommandsFiltered[m_iSelected]);
                        }
                        break;
                    case KeyCode.DownArrow:
                        m_iSelected = Mathf.Clamp(m_iSelected + 1, 0, m_iMenuCommandsFilteredCount - 1);
                        if (_bScrollbar)
                        {
                            this.CheckScrollToSelected();
                        }
                        this.Repaint();
                        break;
                    case KeyCode.UpArrow:
                        m_iSelected = Mathf.Clamp(m_iSelected - 1, 0, m_iMenuCommandsFilteredCount - 1);
                        if (_bScrollbar)
                        {
                            this.CheckScrollToSelected();
                        }
                        this.Repaint();
                        break;
                    case KeyCode.Escape:
                        this.Close();
                        break;
                }

                break;

            case EventType.ScrollWheel:

                if (_bScrollbar)
                {
                    m_fScrollBar += _event.delta.y * c_fButtonHeight;
                    this.Repaint();
                }

                break;

            default:
                break;
        }
    }

    private void Save()
    {
        EditorPrefs.SetString(c_sEditorPrefsPath, this.Serialize(m_lMenuCommandsUsage));
    }

    private void Load()
    {
        m_lMenuCommandsUsage = this.Deserialize(EditorPrefs.GetString(c_sEditorPrefsPath));
    }

    //

    // Internal Helper

    private string Serialize(List<ToolUsage> _lToolUsages)
    {
        m_stringBuilder.Length = 0;
        foreach (ToolUsage toolUsage in _lToolUsages)
        {
            m_stringBuilder.Append(toolUsage.Path.Replace(";", "\\;").Replace(",", "\\,"));
            m_stringBuilder.Append(",,");
            m_stringBuilder.Append(toolUsage.Value);
            m_stringBuilder.Append(";;");
        }
        return m_stringBuilder.ToString();
    }

    private List<ToolUsage> Deserialize(string _string)
    {
        List<ToolUsage> lToolUsages = new List<ToolUsage>();

        string[] arEntries = _string.Split(c_arDeserializeSplitEntries, System.StringSplitOptions.RemoveEmptyEntries);
        foreach (string sEntry in arEntries)
        {
            string[] arEntry = sEntry.Split(c_arDeserializeSplitEntry, System.StringSplitOptions.None);
            if (arEntry.Length >= 2)
            {
                lToolUsages.Add(new ToolUsage(arEntry[0].Replace("\\;", ";").Replace("\\,", ","), int.Parse(arEntry[1])));
            }
        }

        return lToolUsages;
    }

    private string CreateCommandString(string _command, List<string> _list)
    {
        m_stringBuilder.Length = 0;

        foreach (string s in _list)
        {
            m_stringBuilder.Append(s.Trim(' ', '&'));
            m_stringBuilder.Append("/");
        }

        m_stringBuilder.Append(_command.Trim(' ', '&'));

        return m_stringBuilder.ToString();
    }

    private void RecreateCommandList()
    {
        m_lMenuCommands.Clear();

        string sCommands = EditorGUIUtility.SerializeMainMenuToString();
        string[] arCommands = sCommands.Split(new string[] { "\n" }, System.StringSplitOptions.RemoveEmptyEntries);

        List<string> lCurrentParents = new List<string>();
        for (int i = 0, imax = arCommands.Length; i < imax; i++)
        {
            string sCurrentRoot = lCurrentParents.Count != 0 ? lCurrentParents.First() : "";

            string sCurrentLine = arCommands[i];
            string sCurrentLineTrimmed = sCurrentLine.TrimStart(' ');
            int iPreviousIndention = lCurrentParents.Count - 1;
            int iCurrentIndention = (sCurrentLine.Length - sCurrentLineTrimmed.Length) / 4;

            int iLastTab = sCurrentLine.LastIndexOf('\t');
            sCurrentLineTrimmed = iLastTab != -1 ? sCurrentLine.Remove(iLastTab, sCurrentLine.Length - iLastTab).Trim() : sCurrentLine.Trim();

            if (sCurrentLineTrimmed == System.String.Empty)
                continue;

            if (iPreviousIndention > iCurrentIndention)
            {
                EditorFindEditorTools.RemoveLast(lCurrentParents);
                while (iPreviousIndention > iCurrentIndention)
                {
                    EditorFindEditorTools.RemoveLast(lCurrentParents);
                    iPreviousIndention--;
                }

                if (!m_lSkipRoots.Contains(sCurrentRoot))
                {
                    m_lMenuCommands.Add(this.CreateCommandString(sCurrentLineTrimmed, lCurrentParents));
                }
                lCurrentParents.Add(sCurrentLineTrimmed);
            }
            else if (iPreviousIndention < iCurrentIndention)
            {
                if (!m_lSkipRoots.Contains(sCurrentRoot))
                {
                    EditorFindEditorTools.RemoveLast(m_lMenuCommands);
                    m_lMenuCommands.Add(this.CreateCommandString(sCurrentLineTrimmed, lCurrentParents));
                }
                lCurrentParents.Add(sCurrentLineTrimmed);
            }
            else
            {
                EditorFindEditorTools.RemoveLast(lCurrentParents);
                if (!m_lSkipRoots.Contains(sCurrentRoot))
                {
                    m_lMenuCommands.Add(this.CreateCommandString(sCurrentLineTrimmed, lCurrentParents));
                }
                lCurrentParents.Add(sCurrentLineTrimmed);
            }
        }

        this.RecreateFilteredList();
    }

    private void RecreateFilteredList()
    {
        if (System.String.IsNullOrEmpty(m_sSearchString))
        {
            m_lMenuCommandsFiltered = new List<string>(m_lMenuCommands);
        }
        else
        {
            m_lMenuCommandsFiltered.Clear();
            for (int i = 0, imax = m_lMenuCommands.Count; i < imax; i++)
            {
                string sCommand = m_lMenuCommands[i];
                if (EditorFindEditorTools.MatchSearch(sCommand, m_sSearchString))
                {
                    m_lMenuCommandsFiltered.Add(sCommand);
                }
            }
        }

        m_iMenuCommandsFilteredCount = m_lMenuCommandsFiltered.Count;
        this.SortFilteredList();
    }

    private void SortFilteredList()
    {
        m_lMenuCommandsFiltered.Sort();
        m_lMenuCommandsUsage = m_lMenuCommandsUsage.OrderBy(x => x.Value).ThenByDescending(x => x.Path).ToList();

        foreach (ToolUsage toolUsage in m_lMenuCommandsUsage)
        {
            int iIndex = m_lMenuCommandsFiltered.IndexOf(toolUsage.Path);
            if (iIndex != -1 && toolUsage.Value != 0)
            {
                EditorFindEditorTools.MoveToFront(m_lMenuCommandsFiltered, iIndex);
            }
        }
    }

    private bool IsFavorite(string _sCommand)
    {
        int iIndex = -1;
        if ((iIndex = m_lMenuCommandsUsage.FindIndex(x => x.Path == _sCommand)) == -1)
        {
            return false;
        }
        else
        {
            return m_lMenuCommandsUsage[iIndex].Favorite;
        }
    }

    private void ToggleFavorite(string _sCommand)
    {
        int iIndex = -1;
        if ((iIndex = m_lMenuCommandsUsage.FindIndex(x => x.Path == _sCommand)) == -1)
        {
            m_lMenuCommandsUsage.Add(new ToolUsage(_sCommand, 0, true));
        }
        else
        {
            m_lMenuCommandsUsage[iIndex].ToggleFavorite();
        }

        this.Save();
        this.RecreateFilteredList();
        this.Repaint();
    }

    //

    // IHasCustomMenu

    private static readonly GUIContent m_guiContentClearUserData = new GUIContent("Clear User Data");

    public void AddItemsToMenu(GenericMenu menu)
    {
        menu.AddItem(m_guiContentClearUserData, on: false, func: ClearUserData);
    }

    private void ClearUserData()
    {
        m_lMenuCommandsUsage = new List<ToolUsage>();
        this.Save();
        this.RecreateFilteredList();
        this.Repaint();
    }

    //

    // Helper

    public class ToolUsage
    {
        private int m_iValue;

        //

        public string Path
        {
            get;
            private set;
        }

        public int Count
        {
            get { return this.Favorite ? m_iValue ^ (1 << 30) : m_iValue; }
            set { m_iValue = this.CreateValue(value, this.Favorite); }
        }

        public bool Favorite
        {
            get { return (m_iValue & (1 << 30)) != 0; }
            set { m_iValue = this.CreateValue(this.Count, value); }
        }

        public int Value
        {
            get { return m_iValue; }
        }

        //

        public void ToggleFavorite()
        {
            this.Favorite = !this.Favorite;
        }

        private int CreateValue(int _iCount, bool _bFavorite)
        {
            return Mathf.Min(_iCount, (1 << 30) - 1) | (_bFavorite ? (1 << 30) : 0);
        }

        //

        public ToolUsage(string _sPath, int _iCount, bool _bFavorite)
        {
            Path = _sPath;
            m_iValue = this.CreateValue(_iCount, _bFavorite);
        }

        public ToolUsage(string _sPath, int _iValue)
        {
            Path = _sPath;
            m_iValue = _iValue;
        }
    }

    private static void RemoveLast<T>(List<T> _list)
    {
        if (_list == null || _list.Count == 0)
            return;

        _list.RemoveAt(_list.Count - 1);
    }

    private static void MoveToFront<T>(List<T> _list, int _index)
    {
        T item = _list[_index];
        _list.RemoveAt(_index);
        _list.Insert(0, item);
    }

    private static bool MatchSearch(string _string, string _search)
    {
        const char c_chWhiteSpace = ' ';

        string[] arSearchSlices = _search.Split(c_chWhiteSpace);
        _string = _string.ToLowerInvariant();

        foreach (string sSlice in arSearchSlices)
        {
            if (!_string.Contains(sSlice.ToLowerInvariant()))
                return false;
        }

        return true;
    }
}
