/*
MIT License

Copyright (c) 2017 Vicente Russo Neto <vicente.russo@gmail.com>

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

Portions of the software have been adapted from kenlane22
http://answers.unity3d.com/answers/242486/view.html

*/

using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace com.vrusso
{

    
    public class ScriptExecReorderManager : EditorWindow
    {

        private const string PHCLASSNAME = "ScriptExecReorderPlaceHolder"; // Placeholder classname
        private Vector2 _scrollPos;
        private string _filter = "";
        private int _oldIndex = 0;

        private class NameAndType
        {
            public NameAndType(string n, string s) { Name = n; Namespace = s; }
            public readonly string Name;
            public readonly string Namespace;
        }

        // Ignored namespaces
        private static string[] IgnoredNamespaces = new [] { "UnityEngine.EventSystems" };
        private MonoScript[] _allMonoScriptsRuntime;
        private MonoScript _placeholderMonoScript;
        private MonoScript _editMonoScript;
        private int _editMonoScriptOldOrder;
        private readonly List<NameAndType> _typeList = new List<NameAndType>();
        private Dictionary<MonoScript, int> _sortedScripts = new Dictionary<MonoScript, int>();
        private Dictionary<MonoScript, int> _initialSortedScripts = new Dictionary<MonoScript, int>();
        private List<MonoScript> _sortedScriptsIndexedList = new List<MonoScript>();
        private IEnumerable<IGrouping<int, KeyValuePair<MonoScript, int>>> _duplicatedValues;
        private ReorderableList _reordableExecOrderScripts;

        void OnEnable()
        {

            // List all C# class Types that are subclasses of Component
            _typeList.Clear();
            foreach (var type in GetAllSubTypes(typeof(MonoBehaviour)))
            //foreach (var type in GetAllSubTypes(typeof(Component)))
                    _typeList.Add(new NameAndType(type.Name, type.Namespace));
            _typeList.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));

            // Create a mirror of script exec order
            if (_sortedScripts.Count == 0) 
                _sortedScripts = GetSortedDictionary();

            

            CreateList();

        }

        private void CreateList()
        {
            _reordableExecOrderScripts = new ReorderableList(_sortedScripts.Values.ToList(), typeof(Dictionary<MonoScript, int>), true, false, false, true)
            {
                drawElementCallback = DrawElement,
                drawElementBackgroundCallback = DrawElementBackground,
                onSelectCallback = SelectElement,
                onMouseUpCallback = MouseUpElement,
                onReorderCallback = ReorderedElement
            };

        }

        private void ReorderedElement(ReorderableList list)
        {

            var oldMono = _sortedScriptsIndexedList[_oldIndex];
            var tmpSort = _sortedScriptsIndexedList;

            // Realocate to the new position
            tmpSort.Remove(oldMono);
            tmpSort.Insert(list.index, oldMono);

            // Get the previous default time mono index
            var prevDefaultMonoIndex = 0;
            foreach (KeyValuePair<MonoScript, int> mono in _sortedScripts)
            {
                if (mono.Key.GetClass().Name == PHCLASSNAME)
                    break;
                prevDefaultMonoIndex++;
            }

            // Are we moving the default timer?
            var movingDefaultTimer = _oldIndex == prevDefaultMonoIndex;

            if (!movingDefaultTimer)
            {
                var newPositionExecOrder = 0;

                // moving to top-first position
                if (list.index == 0)
                    newPositionExecOrder = _sortedScripts.Values.First() - 100;
                // moving to bottom-last position
                else if (list.index == _sortedScriptsIndexedList.Count - 1)
                    newPositionExecOrder = _sortedScripts.Values.Last() + 100;
                else
                {

                    var prevMono = tmpSort[list.index - 1]; 
                    var nextMono = tmpSort[list.index + 1];
                    var prevOrder = _sortedScripts[prevMono];
                    var nextOrder = _sortedScripts[nextMono];

                    var order = 0;
                    if (nextOrder > prevOrder && prevOrder >= 0)
                    {
                        if (nextOrder - prevOrder > 100)
                            order = nextOrder - 100;
                        else
                            order = prevOrder + (nextOrder - prevOrder) / 2;
                    }
                    else
                    {
                        if (Mathf.Abs(prevOrder) - Mathf.Abs(nextOrder) > 100)
                            order = nextOrder - 100;
                        else
                            order = prevOrder + (Mathf.Abs(prevOrder) - Mathf.Abs(nextOrder)) / 2;
                    }

                    newPositionExecOrder = order;

                }

                // can't be zero
                if (newPositionExecOrder == 0)
                    newPositionExecOrder++;

                // Recreate dictionary based on sort index list
                _sortedScriptsIndexedList = new List<MonoScript>(tmpSort);
                var newSortedDictionary = RecreateDictionary(_sortedScriptsIndexedList);

                // REVIEW: new instance?
                _sortedScripts = new Dictionary<MonoScript, int>(newSortedDictionary);

                // Set the calculated new order to fit between elements
                _sortedScripts[oldMono] = newPositionExecOrder;

            }
            else
            {
                // Moving the default time element
                // Auto ordering for negative and positive ordered scritps

                var newDictionary = new Dictionary<MonoScript,int>();

                // Get the current default time mono index
                var defaultMonoIndex = 0;
                for (int i = 0; i < tmpSort.Count; i++)
                {
                    if (tmpSort[i].GetClass().Name == PHCLASSNAME)
                        break;
                    defaultMonoIndex++;
                }

                var startOrder = defaultMonoIndex * 100 * -1;
                for (int i = 0; i < tmpSort.Count; i++)
                {
                    var mono = tmpSort[i];

                        newDictionary.Add(mono, startOrder);
                        startOrder += 100;
                }

                _sortedScriptsIndexedList = new List<MonoScript>(tmpSort);

                // new instance
                _sortedScripts = new Dictionary<MonoScript, int>(newDictionary);

            }

            ValidateList(); // Check duplicated order

            CreateList(); // Recreate everything
        }

        private Dictionary<MonoScript, int> RecreateDictionary(List<MonoScript> sortedList)
        {
            var newSortedScripts = new Dictionary<MonoScript, int>();
            for (int i = 0; i < sortedList.Count; i++)
            {
                var mono = sortedList[i];
                var order = _sortedScripts[sortedList[i]];
                newSortedScripts.Add(mono, order);
            }

            return newSortedScripts;

        }

        private void ValidateList()
        {
            _duplicatedValues = _sortedScripts.GroupBy(x => x.Value).Where(x => x.Count() > 1);
        }

        private bool DuplicatedValuesContains(int val)
        {
            if (!_duplicatedValues.Any()) return false;
            foreach (var item in _duplicatedValues)
                if (val == item.Key) return true;
            return false;
        }

        private void SelectElement(ReorderableList list)
        {
            _oldIndex = list.index;
        }


        private void MouseUpElement(ReorderableList list)
        {
               //_reordableExecOrderScripts.draggable = true;
        }


        private void DrawElementBackground(Rect rect, int index, bool selected, bool focused)
        {
            if (Event.current.type == EventType.Repaint)
            {
                GUI.enabled = false;
                var elementBackground = new GUIStyle(GUI.skin.textArea);
                elementBackground.Draw(rect, false, selected, selected, focused);
                GUI.enabled = true;
            }
        }

        private void DrawElement(Rect rect, int index, bool active, bool focused)
        {

           
            var lheight = EditorGUIUtility.singleLineHeight;

            MonoScript monoScript;
            try
            {
                monoScript = _sortedScriptsIndexedList[index];
            }
            catch (Exception) { return; }


            var oldval = 0;
            if (monoScript.GetClass().Name != PHCLASSNAME)
            {
                rect.y += 2;
                
                EditorGUI.LabelField(new Rect(rect.x, rect.y, rect.width * 0.75f, lheight), monoScript.GetClass().ToString());

                if(_duplicatedValues != null && _duplicatedValues.Any() && DuplicatedValuesContains(_sortedScripts[monoScript]))
                GUI.color = Color.red;

                EditorGUI.BeginChangeCheck();

                if (oldval == 0)
                    oldval = _sortedScripts[monoScript];

                _sortedScripts[monoScript] = EditorGUI.IntField(new Rect(rect.x + (rect.width * 0.75f), rect.y, rect.width * 0.18f, lheight), _sortedScripts[monoScript]);

                
                if (EditorGUI.EndChangeCheck())
                {

                    if (_editMonoScriptOldOrder == 0)
                        _editMonoScriptOldOrder = oldval;

                    // Mono being manually edited
                    _editMonoScript = monoScript;
                }

                if (GUI.Button(new Rect(rect.x + (rect.width * 0.95f), rect.y, rect.width * 0.95f, lheight), EditorGUIUtility.IconContent("Toolbar Minus"), new GUIStyle(GUI.skin.label)))
                {
                    _sortedScriptsIndexedList.Remove(monoScript);
                    _sortedScripts = RecreateDictionary(_sortedScriptsIndexedList);
                    CreateList();
                }

                GUI.color = Color.white;
            }
            else
            {
                rect.y += 2;
                EditorGUI.LabelField(new Rect(rect.x, rect.y, rect.width, lheight), "Default Time", EditorStyles.centeredGreyMiniLabel);
            }

        }

     
        private void FindNewElementPosition(MonoScript editedMonoScript, int initialOrder, int typedOrder)
        {
            var tmpSortedScripts = new Dictionary<MonoScript, int>(_sortedScripts);

            tmpSortedScripts[editedMonoScript] = initialOrder;

            // Can't be zero
            if (typedOrder == 0)
                typedOrder++;

            var tmpSort = _sortedScriptsIndexedList;

            var newIndex = 0;
            for (int i = 0; i < _sortedScriptsIndexedList.Count; i++)
            {
                var mono = _sortedScriptsIndexedList[i];
                var elementOrder = tmpSortedScripts[mono];

                if (typedOrder > elementOrder)
                    newIndex++;

            }

            if(initialOrder < typedOrder)
                newIndex--;

            if (newIndex < 0) newIndex = 0;

            tmpSort.Remove(editedMonoScript);
            tmpSort.Insert(newIndex, editedMonoScript);

            _sortedScriptsIndexedList = new List<MonoScript>(tmpSort);

            _sortedScripts = RecreateDictionary(_sortedScriptsIndexedList);
            
            Repaint();
        }


        private Dictionary<MonoScript, int> GetSortedDictionary()
        {

            _sortedScriptsIndexedList.Clear();
            _sortedScripts.Clear();
            _initialSortedScripts.Clear();

            // Get all non-default time scripts
            var execScripts = new Dictionary<MonoScript, int>();
            _allMonoScriptsRuntime = MonoImporter.GetAllRuntimeMonoScripts();
            foreach (MonoScript monoScript in _allMonoScriptsRuntime)
            {
                if (monoScript.GetClass() != null && !IgnoredNamespaces.Contains(monoScript.GetClass().Namespace))
                {
                    var currentOrder = MonoImporter.GetExecutionOrder(monoScript);
                    if (currentOrder != 0)
                        execScripts.Add(monoScript, currentOrder);

                    if (monoScript.GetClass().Name == PHCLASSNAME)
                        _placeholderMonoScript = monoScript;

                }
            }

            // Sort the list
            var myList = execScripts.ToList();
            myList.Sort((pair1, pair2) => pair1.Value.CompareTo(pair2.Value));

            // Find the first positive index to insert "default time" divisor
            var positiveIndex = 0;
            for (var index = 0; index < myList.Count; index++)
            {
                KeyValuePair<MonoScript, int> entry = myList[index];
                if (entry.Value > 0)
                {
                    positiveIndex = index;
                    break;
                }
            }

            // Insert a Default time placeholder if positive execution order found
            execScripts.Clear();
            _sortedScriptsIndexedList.Clear();
            for (var index = 0; index < myList.Count; index++)
            {
                var entry = myList[index];
                if (index == positiveIndex - 1)
                {
                    execScripts.Add(AddPlaceHolderToList(), 0);
                }
                else
                {
                    execScripts.Add(entry.Key, entry.Value);
                    _sortedScriptsIndexedList.Add(entry.Key);
                }
            }

            if (positiveIndex == 0)
                execScripts.Add(AddPlaceHolderToList(), 0);


            _initialSortedScripts = new Dictionary<MonoScript, int>(execScripts);

            return execScripts;
        }

        private MonoScript AddPlaceHolderToList()
        {
            _sortedScriptsIndexedList.Add(_placeholderMonoScript);
            return _placeholderMonoScript;
        }

        private bool ExecListContains(string className)
        {
            for (int i = 0; i < _sortedScriptsIndexedList.Count; i++)
            {
                var tmpClassName = _sortedScriptsIndexedList[i].GetClass();
                if (tmpClassName != null && tmpClassName.Name == className)
                    return true;
            }
            return false;
        }

        public static Type[] GetAllSubTypes(Type aBaseClass)
        {
            var result = new List<Type>();
            var AS = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var A in AS)
            {
                var types = A.GetTypes();
                foreach (var T in types)
                {
                    if(!IgnoredNamespaces.Contains(T.Namespace) && T.IsSubclassOf(aBaseClass))
                        result.Add(T);
                }
            }
            return result.ToArray();
        }

        void OnGUI()
        {
            if (Event.current.type == EventType.KeyUp && Event.current.keyCode == KeyCode.Return)
            {
                FindNewElementPosition(_editMonoScript, _editMonoScriptOldOrder, _sortedScripts[_editMonoScript]);

                // clear
                _editMonoScript = null;
                _editMonoScriptOldOrder = 0;
            }

            GUILayout.BeginHorizontal();
            {

                GUILayout.BeginVertical(GUILayout.Width(250));
                {

                    GUILayout.Space(8);
                    GUILayout.Label("Search Component:", GUILayout.Width(250));
                    _filter = GUILayout.TextField(_filter);
                    GUILayout.Space(4);

                    _scrollPos = GUILayout.BeginScrollView(_scrollPos);
                    {
                        int n = _typeList.Count;
                        for (int i = 0; i < n; i++)
                        {
                            string nm = _typeList[i].Name;
                            string nmspace = _typeList[i].Namespace;
                            if (nm.ToUpper().Contains(_filter.ToUpper()) && !ExecListContains(nm))
                            {
                                GUILayout.BeginHorizontal();
                                // TODO: Display namespace with toggle
                                GUILayout.Label(nm, GUILayout.Width(192));
                                if (GUILayout.Button("->"))
                                {
                                    for (int j = 0; j < _allMonoScriptsRuntime.Length; j++)
                                    {
                                        var monoScript = _allMonoScriptsRuntime[j];
                                        if (monoScript.GetClass() != null && !ExecListContains(nm) && monoScript.GetClass().Name == nm && monoScript.GetClass().Namespace == nmspace)
                                        {
                                            var lastExecOrder = _sortedScripts.Values.Last();
                                            _sortedScriptsIndexedList.Add(monoScript);
                                            _sortedScripts.Add(monoScript, lastExecOrder+100);
                                            CreateList();
                                        }
                                    }
                                }
                                GUILayout.EndHorizontal();
                            }
                        }
                    }
                    GUILayout.EndScrollView();
                }
                GUILayout.EndVertical();

                const int rightSidebarWidth = 400;
                GUILayout.BeginVertical(GUILayout.Width(rightSidebarWidth));
                {

                    EditorGUILayout.Space();
                    EditorGUILayout.HelpBox("Add scripts to the custom order and drag them to reorder.\n\nScripts in the custom order can execute before or after the default time and are executed from top to bottom. All other scripts execut at the default time in the order they are loaded.\n\n(Changing the order of a scripts may modify the meta data for more than one script.)", MessageType.None);
                    EditorGUILayout.Space();

                    _reordableExecOrderScripts.DoLayoutList();

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(rightSidebarWidth - 120);
                    if (GUILayout.Button("Revert", GUILayout.Width(60)))
                    {
                        _sortedScripts = GetSortedDictionary();
                        CreateList();
                    }

                    if (_duplicatedValues != null && _duplicatedValues.Any()) GUI.enabled = false;
                    if (GUILayout.Button("Apply", GUILayout.Width(60)))
                    {

                        // Delete
                        var currDic = new Dictionary<MonoScript,int>(_initialSortedScripts);
                        foreach (var entry in currDic)
                        {
                            if (!_sortedScripts.ContainsKey(entry.Key))
                                MonoImporter.SetExecutionOrder(entry.Key, 0);
                        }

                        // Change order
                        foreach (var entry in _sortedScripts)
                        {
                            var mono = entry.Key;
                            var order = entry.Value; // modified order

                            if (currDic.ContainsKey(mono))
                            {
                                var initialOrder = currDic[mono]; // former order of same script

                                // if not equal, change
                                if (order != initialOrder)
                                    MonoImporter.SetExecutionOrder(mono, order);
                            }
                            else
                            {
                                MonoImporter.SetExecutionOrder(mono, order);
                            }
                            
                        }

                    }
                    GUI.enabled = true;
                    EditorGUILayout.EndHorizontal();

                }
                GUILayout.EndVertical();
            }
            GUILayout.EndHorizontal();
        }

        [MenuItem("Tools/Script Execution Order Manager")]
        static public void Init()
        {
            var editorWindow = GetWindow(typeof(ScriptExecReorderManager)) as ScriptExecReorderManager;
            editorWindow.autoRepaintOnSceneChange = true;
            editorWindow.Show();
        }

    }

}