using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LevelDesignTools
{
    /// <summary>
    /// Panel in-editor pour generer des salles.
    /// Trois modes :
    ///  - "Boite" : salle rectangulaire (4 murs + sol + plafond), taille/position/epaisseur reglables.
    ///  - "Dessin libre" : on dessine un contour au sol dans la vue Scene (clics successifs),
    ///    puis on l'extrude en 3D (sol/plafond triangules + murs suivant chaque segment)
    ///    avec une hauteur et une direction (vers le haut ou vers le bas) au choix.
    ///  - "Decoupe" : on selectionne un mur/sol/plafond genere par cet outil (une primitive
    ///    Cube), on dessine un contour directement sur sa surface, et on decoupe un trou
    ///    (porte, fenetre...) a la forme dessinee.
    /// Equivalent Unity du RoomGenerator (generate_room) construit precedemment sur les projets Unreal.
    /// </summary>
    public class RoomBuilderWindow : EditorWindow
    {
        private enum Mode { Box, Freehand, Cut }
        private enum ExtrudeDirection { Up, Down }

        private const string RootFolderName = "Rooms";
        private const string GeneratedMeshFolder = "Assets/GeneratedMeshes";
        private const float ClosePointScreenDistance = 12f; // px, pour fermer la boucle en cliquant pres du 1er point

        private Mode _mode = Mode.Box;

        // ===================== MODE "BOITE" =====================
        private string _roomName = "Room";
        private Vector3 _position = Vector3.zero;
        private float _sizeX = 8f;   // largeur (X)
        private float _sizeZ = 6f;   // profondeur (Z)
        private float _height = 3f;  // hauteur (Y)
        private float _wallThickness = 0.2f;
        private bool _addCeiling = true;

        // ===================== MODE "DESSIN LIBRE" =====================
        private string _fpRoomName = "Room";
        private readonly List<Vector3> _fpPoints = new List<Vector3>();
        private float _fpGroundY = 0f;
        private float _fpHeight = 3f;
        private ExtrudeDirection _fpDirection = ExtrudeDirection.Up;
        private float _fpWallThickness = 0.2f;
        private bool _fpAddFloor = true;
        private bool _fpAddCeiling = true;
        private bool _isDrawingActive = false;
        private bool _fpAdjustingHeight = false;   // contour fermé : la hauteur suit la souris
        private Vector3 _fpHeightPivot;            // centre du contour, au niveau du plan de dessin
        private Tool _toolBeforeDrawing = Tool.Move;

        // ===================== MODE "DECOUPE" =====================
        private GameObject _cutTarget;
        private GameObject _cutHover;   // objet sous le curseur tant qu'aucun point n'est posé
        private readonly List<Vector3> _cutPoints = new List<Vector3>(); // points monde, sur la surface de _cutTarget
        private bool _isCuttingActive = false;
        private Tool _toolBeforeCutting = Tool.Move;

        [MenuItem("Tools/Level Design/Room Builder")]
        public static void ShowWindow()
        {
            var window = GetWindow<RoomBuilderWindow>();
            window.titleContent = new GUIContent("Room Builder");
            window.minSize = new Vector2(340, 440);
            window.Show();
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            if (_isDrawingActive)
            {
                StopDrawing(restoreTool: true);
            }
            if (_isCuttingActive)
            {
                StopCutting(restoreTool: true);
            }
        }

        private void OnGUI()
        {
            _mode = (Mode)GUILayout.Toolbar((int)_mode, new[] { "Boîte (dimensions)", "Dessin libre (contour)", "Découpe (trous)" });
            EditorGUILayout.Space(8);

            switch (_mode)
            {
                case Mode.Box:
                    DrawBoxModeGUI();
                    break;
                case Mode.Freehand:
                    DrawFreehandModeGUI();
                    break;
                case Mode.Cut:
                    DrawCutModeGUI();
                    break;
            }
        }

        // ===================== MODE BOITE =====================

        private void DrawBoxModeGUI()
        {
            EditorGUILayout.LabelField("Générateur de salle (boîte)", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            _roomName = EditorGUILayout.TextField("Nom de la salle", _roomName);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Position (centre, au sol)", EditorStyles.boldLabel);
            _position = EditorGUILayout.Vector3Field(GUIContent.none, _position);

            if (GUILayout.Button("Caler sur le pivot de la vue Scene"))
            {
                SnapPositionToSceneView(ref _position);
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Dimensions", EditorStyles.boldLabel);
            _sizeX = EditorGUILayout.FloatField("Largeur (X)", _sizeX);
            _sizeZ = EditorGUILayout.FloatField("Profondeur (Z)", _sizeZ);
            _height = EditorGUILayout.FloatField("Hauteur (Y)", _height);
            _wallThickness = EditorGUILayout.FloatField("Épaisseur des murs", _wallThickness);
            _addCeiling = EditorGUILayout.Toggle("Générer un plafond", _addCeiling);

            EditorGUILayout.Space(4);
            if (_sizeX <= _wallThickness * 2f || _sizeZ <= _wallThickness * 2f || _height <= 0f || _wallThickness <= 0f)
            {
                EditorGUILayout.HelpBox(
                    "Dimensions invalides : la largeur et la profondeur doivent être strictement supérieures à 2x l'épaisseur des murs, hauteur et épaisseur > 0.",
                    MessageType.Warning);
            }

            EditorGUILayout.Space(12);
            GUI.enabled = IsBoxConfigValid();
            if (GUILayout.Button("Générer la salle", GUILayout.Height(32)))
            {
                GenerateBoxRoom();
            }
            GUI.enabled = true;
        }

        private bool IsBoxConfigValid()
        {
            return _sizeX > _wallThickness * 2f
                && _sizeZ > _wallThickness * 2f
                && _height > 0f
                && _wallThickness > 0f;
        }

        private void GenerateBoxRoom()
        {
            Transform root = GetOrCreateRootFolder();
            string uniqueName = GameObjectUtility.GetUniqueNameForSibling(root, string.IsNullOrEmpty(_roomName) ? "Room" : _roomName);

            Undo.SetCurrentGroupName($"Generate Room {uniqueName}");
            int undoGroup = Undo.GetCurrentGroup();

            var roomRoot = new GameObject(uniqueName);
            Undo.RegisterCreatedObjectUndo(roomRoot, "Create Room Root");
            roomRoot.transform.SetParent(root, worldPositionStays: true);
            roomRoot.transform.position = _position;

            float t = _wallThickness;
            float halfX = _sizeX * 0.5f;
            float halfZ = _sizeZ * 0.5f;

            // Sol
            CreateBoxPart(roomRoot.transform, "Floor",
                localPosition: new Vector3(0f, -t * 0.5f, 0f),
                size: new Vector3(_sizeX, t, _sizeZ));

            // Plafond (optionnel)
            if (_addCeiling)
            {
                CreateBoxPart(roomRoot.transform, "Ceiling",
                    localPosition: new Vector3(0f, _height + t * 0.5f, 0f),
                    size: new Vector3(_sizeX, t, _sizeZ));
            }

            // Murs (Nord/Sud le long de Z, Est/Ouest le long de X)
            CreateBoxPart(roomRoot.transform, "Wall_North",
                localPosition: new Vector3(0f, _height * 0.5f, halfZ - t * 0.5f),
                size: new Vector3(_sizeX, _height, t));

            CreateBoxPart(roomRoot.transform, "Wall_South",
                localPosition: new Vector3(0f, _height * 0.5f, -(halfZ - t * 0.5f)),
                size: new Vector3(_sizeX, _height, t));

            CreateBoxPart(roomRoot.transform, "Wall_East",
                localPosition: new Vector3(halfX - t * 0.5f, _height * 0.5f, 0f),
                size: new Vector3(t, _height, _sizeZ));

            CreateBoxPart(roomRoot.transform, "Wall_West",
                localPosition: new Vector3(-(halfX - t * 0.5f), _height * 0.5f, 0f),
                size: new Vector3(t, _height, _sizeZ));

            Undo.CollapseUndoOperations(undoGroup);

            Selection.activeGameObject = roomRoot;
            EditorGUIUtility.PingObject(roomRoot);

            Debug.Log($"[RoomBuilder] Salle '{uniqueName}' générée à {_position} " +
                      $"({_sizeX}x{_height}x{_sizeZ}, murs {t}).");
        }

        // ===================== MODE DESSIN LIBRE =====================

        private void DrawFreehandModeGUI()
        {
            EditorGUILayout.LabelField("Dessin libre du contour (vue Scene)", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            _fpRoomName = EditorGUILayout.TextField("Nom de la salle", _fpRoomName);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Plan de dessin", EditorStyles.boldLabel);
            _fpGroundY = EditorGUILayout.FloatField("Hauteur du plan (Y)", _fpGroundY);
            if (GUILayout.Button("Caler la hauteur sur le pivot de la vue Scene"))
            {
                var sceneView = SceneView.lastActiveSceneView;
                if (sceneView == null)
                {
                    Debug.LogWarning("[RoomBuilder] Aucune vue Scene active à utiliser comme référence.");
                }
                else
                {
                    _fpGroundY = sceneView.pivot.y;
                    Repaint();
                }
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Extrusion", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(_fpAdjustingHeight))
            {
                _fpHeight = EditorGUILayout.FloatField("Hauteur", _fpHeight);
            }
            if (_fpAdjustingHeight)
            {
                EditorGUILayout.LabelField("Sens", _fpDirection == ExtrudeDirection.Up ? "vers le haut" : "vers le bas");
            }
            _fpWallThickness = EditorGUILayout.FloatField("Épaisseur des murs", _fpWallThickness);
            _fpAddFloor = EditorGUILayout.Toggle("Générer un sol", _fpAddFloor);
            _fpAddCeiling = EditorGUILayout.Toggle("Générer un plafond", _fpAddCeiling);

            EditorGUILayout.Space(12);

            EditorGUILayout.BeginHorizontal();
            bool wasDrawing = _isDrawingActive;
            bool nowDrawing = GUILayout.Toggle(_isDrawingActive, _isDrawingActive ? "Dessin en cours…" : "Activer le mode dessin", "Button", GUILayout.Height(28));
            if (nowDrawing != wasDrawing)
            {
                if (nowDrawing) StartDrawing();
                else StopDrawing(restoreTool: true);
            }

            GUI.enabled = _fpPoints.Count > 0;
            if (GUILayout.Button("Effacer le contour", GUILayout.Height(28)))
            {
                _fpPoints.Clear();
                SceneView.RepaintAll();
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField($"Points placés : {_fpPoints.Count}");

            if (_isDrawingActive)
            {
                EditorGUILayout.HelpBox(
                    "Dans la vue Scene : clic gauche = ajouter un point sur le plan au sol.\n" +
                    "Clic près du 1er point, ou touche Entrée = fermer le contour.\n" +
                    "La boîte apparaît alors et sa hauteur suit la souris : clic gauche pour valider, " +
                    "Ctrl pour aimanter sur la grille, Échap pour revenir au contour.\n" +
                    "Retour arrière = supprimer le dernier point.",
                    MessageType.Info);
            }
            else if (_fpPoints.Count > 0 && _fpPoints.Count < 3)
            {
                EditorGUILayout.HelpBox("Il faut au moins 3 points pour former un contour fermé.", MessageType.Warning);
            }

            EditorGUILayout.Space(8);
            GUI.enabled = IsFreehandConfigValid();
            if (GUILayout.Button("Fermer le contour & régler la hauteur", GUILayout.Height(32)))
            {
                BeginHeightAdjust();
            }
            GUI.enabled = true;
        }

        private bool IsFreehandConfigValid()
        {
            return _fpPoints.Count >= 3 && _fpHeight > 0f && _fpWallThickness > 0f;
        }

        private void StartDrawing()
        {
            _isDrawingActive = true;
            _fpPoints.Clear();
            _toolBeforeDrawing = Tools.current;
            Tools.current = Tool.None;
            SceneView.RepaintAll();
            Debug.Log("[RoomBuilder] Mode dessin activé : cliquez dans la vue Scene pour placer les points du contour.");
        }

        private void StopDrawing(bool restoreTool)
        {
            _isDrawingActive = false;
            _fpAdjustingHeight = false;
            if (restoreTool)
            {
                Tools.current = _toolBeforeDrawing;
            }
            SceneView.RepaintAll();
            Repaint();
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (_fpAdjustingHeight)
            {
                HandleHeightAdjustSceneGUI(sceneView);
            }
            else if (_isDrawingActive)
            {
                HandleFreehandSceneGUI(sceneView);
            }
            else if (_isCuttingActive)
            {
                HandleCutSceneGUI(sceneView);
            }
        }

        private void HandleFreehandSceneGUI(SceneView sceneView)
        {
            Event e = Event.current;
            int controlID = GUIUtility.GetControlID(FocusType.Passive);

            // Empêche la sélection/manipulation normale de la vue Scene pendant le dessin.
            if (e.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(controlID);
            }

            Plane groundPlane = new Plane(Vector3.up, new Vector3(0f, _fpGroundY, 0f));
            Ray mouseRay = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            bool hasGroundPoint = groundPlane.Raycast(mouseRay, out float enter);
            Vector3 groundPoint = hasGroundPoint ? mouseRay.GetPoint(enter) : Vector3.zero;

            bool nearFirstPoint = false;
            if (hasGroundPoint && _fpPoints.Count >= 3)
            {
                Vector2 firstScreen = HandleUtility.WorldToGUIPoint(_fpPoints[0]);
                nearFirstPoint = Vector2.Distance(firstScreen, e.mousePosition) <= ClosePointScreenDistance;
            }

            // --- Dessin des points/segments déjà placés + prévisualisation ---
            Handles.color = Color.yellow;
            for (int i = 0; i < _fpPoints.Count; i++)
            {
                float size = HandleUtility.GetHandleSize(_fpPoints[i]) * 0.08f;
                Handles.SphereHandleCap(0, _fpPoints[i], Quaternion.identity, size, EventType.Repaint);
            }
            if (_fpPoints.Count > 1)
            {
                Handles.DrawAAPolyLine(4f, _fpPoints.ToArray());
            }
            if (hasGroundPoint && _fpPoints.Count > 0)
            {
                Handles.color = nearFirstPoint ? Color.green : new Color(1f, 1f, 0f, 0.6f);
                Vector3 previewTarget = nearFirstPoint ? _fpPoints[0] : groundPoint;
                Handles.DrawDottedLine(_fpPoints[_fpPoints.Count - 1], previewTarget, 4f);
                Handles.Label(groundPoint + Vector3.up * 0.15f, groundPoint.ToString("F2"));
            }

            // --- Gestion des events ---
            if (e.type == EventType.MouseMove)
            {
                sceneView.Repaint();
            }
            else if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                if (hasGroundPoint)
                {
                    if (nearFirstPoint)
                    {
                        BeginHeightAdjust();
                    }
                    else
                    {
                        _fpPoints.Add(groundPoint);
                        Repaint();
                        sceneView.Repaint();
                    }
                }
                e.Use();
            }
            else if (e.type == EventType.KeyDown)
            {
                if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && _fpPoints.Count >= 3)
                {
                    BeginHeightAdjust();
                    e.Use();
                }
                else if (e.keyCode == KeyCode.Escape)
                {
                    _fpPoints.Clear();
                    StopDrawing(restoreTool: true);
                    e.Use();
                }
                else if (e.keyCode == KeyCode.Backspace && _fpPoints.Count > 0)
                {
                    _fpPoints.RemoveAt(_fpPoints.Count - 1);
                    Repaint();
                    sceneView.Repaint();
                    e.Use();
                }
            }
        }

        /// <summary>
        /// Le contour est fermé : la boîte apparaît et sa hauteur suit la souris jusqu'au clic de
        /// validation. Régler une hauteur revient à saisir la géométrie plutôt qu'à taper un
        /// nombre puis à vérifier le résultat.
        /// </summary>
        private void BeginHeightAdjust()
        {
            if (_fpPoints.Count < 3)
            {
                Debug.LogWarning("[RoomBuilder] Il faut au moins 3 points pour fermer un contour.");
                return;
            }

            Vector3 centroid = Vector3.zero;
            foreach (var p in _fpPoints) centroid += p;
            centroid /= _fpPoints.Count;
            centroid.y = _fpGroundY;

            _fpHeightPivot = centroid;
            _fpAdjustingHeight = true;
            SceneView.RepaintAll();
            Repaint();

            Debug.Log("[RoomBuilder] Contour fermé : règle la hauteur à la souris, clic gauche pour valider. " +
                      "Ctrl aimante sur la grille, Échap revient au contour.");
        }

        private void HandleHeightAdjustSceneGUI(SceneView sceneView)
        {
            Event e = Event.current;
            int controlID = GUIUtility.GetControlID(FocusType.Passive);

            if (e.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(controlID);
                return;
            }

            // Plan vertical passant par le centre du contour et faisant face à la caméra : le
            // curseur y glisse naturellement de haut en bas quel que soit l'angle de vue. Une
            // simple mesure du déplacement vertical à l'écran dépendrait du zoom et donnerait une
            // sensibilité différente à chaque distance.
            Camera camera = sceneView.camera;
            Vector3 facing = camera != null ? camera.transform.forward : Vector3.forward;
            facing.y = 0f;
            if (facing.sqrMagnitude < 1e-6f) facing = Vector3.forward;   // vue plongeante à la verticale
            facing.Normalize();

            var plane = new Plane(-facing, _fpHeightPivot);
            Ray mouseRay = HandleUtility.GUIPointToWorldRay(e.mousePosition);

            float enter;
            if (plane.Raycast(mouseRay, out enter))
            {
                float y = mouseRay.GetPoint(enter).y;
                float signed = y - _fpGroundY;

                if (e.control || e.command)
                {
                    float step = Mathf.Abs(EditorSnapSettings.move.y);
                    if (step > 1e-4f) signed = Mathf.Round(signed / step) * step;
                }

                _fpDirection = signed >= 0f ? ExtrudeDirection.Up : ExtrudeDirection.Down;
                _fpHeight = Mathf.Max(Mathf.Abs(signed), 0.01f);
            }

            DrawHeightPreview();

            if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag)
            {
                sceneView.Repaint();
                Repaint();
            }
            else if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                _fpAdjustingHeight = false;
                GenerateFreehandRoom();
                e.Use();
            }
            else if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    _fpAdjustingHeight = false;
                    GenerateFreehandRoom();
                    e.Use();
                }
                else if (e.keyCode == KeyCode.Escape)
                {
                    // Retour à l'édition du contour, sans rien perdre.
                    _fpAdjustingHeight = false;
                    SceneView.RepaintAll();
                    Repaint();
                    e.Use();
                }
            }
        }

        private void DrawHeightPreview()
        {
            float signed = _fpHeight * (_fpDirection == ExtrudeDirection.Up ? 1f : -1f);
            int count = _fpPoints.Count;

            var bottom = new Vector3[count + 1];
            var top = new Vector3[count + 1];
            for (int i = 0; i <= count; i++)
            {
                Vector3 p = _fpPoints[i % count];
                bottom[i] = new Vector3(p.x, _fpGroundY, p.z);
                top[i] = new Vector3(p.x, _fpGroundY + signed, p.z);
            }

            Handles.color = new Color(1f, 0.85f, 0.2f, 0.9f);
            Handles.DrawAAPolyLine(3f, bottom);
            Handles.DrawAAPolyLine(3f, top);

            Handles.color = new Color(1f, 0.85f, 0.2f, 0.5f);
            for (int i = 0; i < count; i++) Handles.DrawLine(bottom[i], top[i]);

            Vector3 label = new Vector3(_fpHeightPivot.x, _fpGroundY + signed, _fpHeightPivot.z);
            Handles.color = Color.white;
            Handles.Label(label, $"{Mathf.Abs(signed):F2} m  ({(signed >= 0f ? "vers le haut" : "vers le bas")})");
        }

        // ===================== MODE DECOUPE =====================

        private void DrawCutModeGUI()
        {
            EditorGUILayout.LabelField("Découpe d'un trou (porte, fenêtre…)", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "Active le mode, puis dessine directement sur la surface voulue : la cible est " +
                "celle qui se trouve sous le curseur. Fonctionne sur les murs, les sols et les " +
                "plafonds générés par cet outil, et sur toute boîte — y compris venue d'ailleurs. " +
                "Une cible déjà percée reste découpable : les trous s'accumulent.",
                MessageType.None);

            EditorGUILayout.Space(8);

            RoomPanel panel = _cutTarget != null ? _cutTarget.GetComponent<RoomPanel>() : null;
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("Cible", _cutTarget, typeof(GameObject), true);
            }
            if (panel != null)
            {
                EditorGUILayout.LabelField("Trous déjà percés", panel.Holes.Count.ToString());
            }

            EditorGUILayout.Space(12);

            EditorGUILayout.BeginHorizontal();
            bool wasCutting = _isCuttingActive;
            bool nowCutting = GUILayout.Toggle(_isCuttingActive, _isCuttingActive ? "Découpe en cours…" : "Activer le mode découpe", "Button", GUILayout.Height(28));
            if (nowCutting != wasCutting)
            {
                if (nowCutting) StartCutting();
                else StopCutting(restoreTool: true);
            }

            GUI.enabled = _cutPoints.Count > 0;
            if (GUILayout.Button("Effacer le contour", GUILayout.Height(28)))
            {
                _cutPoints.Clear();
                SceneView.RepaintAll();
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField($"Points placés : {_cutPoints.Count}");

            if (_isCuttingActive)
            {
                EditorGUILayout.HelpBox(
                    "Dans la vue Scene : clic gauche sur une surface = poser un point. Le premier " +
                    "clic verrouille la cible, les suivants doivent rester dessus.\n" +
                    "Clic près du 1er point, ou touche Entrée = fermer le contour et découper.\n" +
                    "Retour arrière = supprimer le dernier point. Échap = quitter le mode découpe.\n" +
                    "Le contour doit rester entièrement à l'intérieur de la surface visée, sans " +
                    "chevaucher un trou déjà percé.",
                    MessageType.Info);
            }

            EditorGUILayout.Space(8);
            GUI.enabled = _cutTarget != null && _cutPoints.Count >= 3;
            if (GUILayout.Button("Fermer & découper", GUILayout.Height(32)))
            {
                PerformCut();
            }
            GUI.enabled = true;

            GUI.enabled = panel != null && panel.Holes.Count > 0;
            if (GUILayout.Button("Retirer le dernier trou", GUILayout.Height(24)))
            {
                RemoveLastHole();
            }
            GUI.enabled = true;
        }

        /// <summary>
        /// Une cible est découpable si c'est une primitive Cube encore intacte, OU un panneau déjà
        /// converti par cet outil (il porte alors un <see cref="RoomPanel"/>).
        ///
        /// Bug historique : le test portait sur le NOM du mesh (== "Cube"). Or la découpe remplace
        /// le mesh partagé du Cube par un mesh généré portant un autre nom : le mur devenait donc
        /// définitivement non découpable dès la première ouverture percée. Le nom du mesh n'est
        /// plus un critère.
        /// </summary>
        private static bool IsValidCutTarget(GameObject target, out string reason)
        {
            if (target == null)
            {
                reason = "aucune cible sélectionnée.";
                return false;
            }

            if (target.GetComponent<RoomPanel>() != null)
            {
                reason = null;
                return true;
            }

            var meshFilter = target.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                reason = "l'objet n'a pas de MeshFilter (ce n'est pas un mur/sol/plafond).";
                return false;
            }

            if (!meshFilter.sharedMesh.isReadable)
            {
                reason = $"le mesh « {meshFilter.sharedMesh.name} » n'est pas lisible. Coche " +
                         "Read/Write dans ses paramètres d'import pour pouvoir le découper.";
                return false;
            }

            if (IsBoxMesh(meshFilter.sharedMesh))
            {
                reason = null;
                return true;
            }

            reason = $"le mesh « {meshFilter.sharedMesh.name} » n'a pas la forme d'une boîte et ne " +
                     "porte pas de Room Panel. La découpe reconstruit un panneau plat extrudé : " +
                     "appliquée à une forme quelconque, elle la remplacerait par une boîte trouée.";
            return false;
        }

        /// <summary>
        /// Vrai si le mesh EST sa boîte englobante : chacun de ses triangles repose entièrement
        /// sur l'une des six faces de cette boîte.
        ///
        /// Le test portait d'abord sur le NOM du mesh (« Cube »), ce qui excluait tout cube venu
        /// d'ailleurs. Puis sur ses sommets, pris un par un : « chaque sommet est-il sur une des
        /// six faces ? ». C'était nécessaire mais pas suffisant, et un essai dans l'éditeur l'a
        /// montré — le cylindre d'Unity passait, tous ses sommets étant posés sur les plans du
        /// haut et du bas. Le découper l'aurait remplacé par une boîte. Raisonner par triangle
        /// tranche : la paroi d'un cylindre relie le plan du haut à celui du bas sans reposer sur
        /// aucun des deux, alors qu'une face de boîte, même subdivisée, reste dans son plan.
        /// </summary>
        private static bool IsBoxMesh(Mesh mesh)
        {
            if (mesh == null || mesh.vertexCount == 0) return false;

            // Le test parcourt toute la géométrie, et la validation de la cible est réévaluée à
            // chaque déplacement de la souris : sans mémoire, un mesh dense coûterait cher.
            bool cached;
            if (BoxMeshCache.TryGetValue(mesh, out cached)) return cached;

            Bounds bounds = mesh.bounds;
            Vector3 extents = bounds.extents;

            // Une boîte a trois dimensions non nulles : un quad, une surface plate, n'en est pas une.
            if (extents.x <= 1e-6f || extents.y <= 1e-6f || extents.z <= 1e-6f)
            {
                BoxMeshCache[mesh] = false;
                return false;
            }

            float tolerance = Mathf.Max(extents.x, Mathf.Max(extents.y, extents.z)) * 1e-3f;
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            bool isBox = true;

            for (int i = 0; i + 2 < triangles.Length && isBox; i += 3)
            {
                Vector3 a = vertices[triangles[i]];
                Vector3 b = vertices[triangles[i + 1]];
                Vector3 c = vertices[triangles[i + 2]];

                bool onSomeFace = false;
                for (int axis = 0; axis < 3 && !onSomeFace; axis++)
                {
                    for (int side = -1; side <= 1 && !onSomeFace; side += 2)
                    {
                        float plane = bounds.center[axis] + side * extents[axis];
                        onSomeFace = Mathf.Abs(a[axis] - plane) <= tolerance
                                  && Mathf.Abs(b[axis] - plane) <= tolerance
                                  && Mathf.Abs(c[axis] - plane) <= tolerance;
                    }
                }

                if (!onSomeFace) isBox = false;
            }

            BoxMeshCache[mesh] = isBox;
            return isBox;
        }

        private static readonly Dictionary<Mesh, bool> BoxMeshCache = new Dictionary<Mesh, bool>();

        /// <summary>
        /// Tout ce qu'il faut savoir d'une cible pour y placer des points et y percer un trou :
        /// son repère, son contour extérieur en unités monde, ses deux plans de face.
        ///
        /// La vue Scene et la découpe s'en servent toutes les deux — la première pour projeter le
        /// curseur sur la bonne face et dire si le point tombe dans la matière, la seconde pour
        /// valider le contour. Les faire diverger, c'est promettre à l'écran ce que la découpe
        /// refusera ensuite.
        /// </summary>
        private struct CutSurface
        {
            public GameObject Target;
            public Transform Transform;
            public RoomPanel Panel;
            public Vector3 Scale;
            public Vector3 PanelSize;
            public int UAxis, VAxis, WAxis;
            public List<Vector2> Outer;      // contour extérieur, unités monde
            public float HalfW;              // demi-épaisseur, unités monde

            /// <summary>
            /// Décalage du mesh par rapport au pivot, en espace mesh. Nul pour un panneau, qui est
            /// centré par construction ; pas forcément pour une boîte venue d'ailleurs, dont la
            /// géométrie peut être posée à côté de son pivot. L'ignorer placerait les points de
            /// découpe à côté de la surface visée.
            /// </summary>
            public Vector3 LocalCenter;
        }

        private static bool TryDescribeCutSurface(GameObject target, out CutSurface surface)
        {
            surface = default(CutSurface);
            if (target == null) return false;
            if (!IsValidCutTarget(target, out _)) return false;

            Transform t = target.transform;
            RoomPanel panel = target.GetComponent<RoomPanel>();
            Vector3 scale = t.localScale;

            // Taille du panneau dans le monde = taille du mesh x échelle du transform. La formule
            // couvre les deux cas sans distinction : boîte brute (mesh unitaire ou non, échelle
            // portant les dimensions) et panneau déjà découpé (mesh à taille réelle, échelle 1 —
            // ou remise à l'échelle à la main entre deux découpes, auquel cas on la « cuit »).
            Vector3 meshSize = panel != null ? panel.Size : MeshBoxSize(target);
            Vector3 localCenter = panel != null ? Vector3.zero : MeshBoxCenter(target);
            var panelSize = new Vector3(meshSize.x * scale.x, meshSize.y * scale.y, meshSize.z * scale.z);

            int uAxis, vAxis, wAxis;
            if (panel != null)
            {
                // Repère conservé tel quel : les trous déjà percés y sont exprimés.
                uAxis = panel.UAxis; vAxis = panel.VAxis; wAxis = panel.WAxis;
            }
            else
            {
                PanelMeshBuilder.GetFlatAxes(panelSize, out uAxis, out vAxis, out wAxis);
            }

            float halfW = Mathf.Abs(panelSize[wAxis]) * 0.5f;
            if (halfW < 0.001f) return false;

            var outer = new List<Vector2>();
            if (panel != null)
            {
                foreach (var p in panel.ResolveOuterContour())
                {
                    outer.Add(new Vector2(p.x * scale[uAxis], p.y * scale[vAxis]));
                }
            }
            else
            {
                outer = RectangleContour(panelSize, uAxis, vAxis);
            }
            if (outer.Count < 3) return false;

            surface = new CutSurface
            {
                Target = target, Transform = t, Panel = panel, Scale = scale, PanelSize = panelSize,
                UAxis = uAxis, VAxis = vAxis, WAxis = wAxis, Outer = outer, HalfW = halfW,
                LocalCenter = localCenter,
            };
            return true;
        }

        /// <summary>Dimensions de la boîte englobante du mesh, pour une cible pas encore convertie en panneau.</summary>
        private static Vector3 MeshBoxSize(GameObject target)
        {
            var meshFilter = target.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null) return Vector3.one;
            return meshFilter.sharedMesh.bounds.size;
        }

        private static Vector3 MeshBoxCenter(GameObject target)
        {
            var meshFilter = target.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null) return Vector3.zero;
            return meshFilter.sharedMesh.bounds.center;
        }

        /// <summary>
        /// Projette le rayon de la souris sur la face de la cible qui regarde la caméra, et rend
        /// le point en coordonnées (U,V) du panneau. Renvoie false si le rayon manque la surface
        /// ou tombe hors de la matière (au-delà du contour, ou dans un trou déjà percé).
        /// </summary>
        private static bool TryProjectOnSurface(CutSurface surface, Ray ray, out Vector3 worldPoint, out Vector2 uv)
        {
            worldPoint = Vector3.zero;
            uv = Vector2.zero;

            Vector3 axis = Vector3.zero;
            axis[surface.WAxis] = 1f;
            Vector3 normal = surface.Transform.TransformDirection(axis).normalized;

            // La face tournée vers la caméra : celle dont la normale s'oppose au rayon.
            float side = Vector3.Dot(normal, ray.direction) < 0f ? 1f : -1f;

            Vector3 local = surface.LocalCenter;
            local[surface.WAxis] += side * surface.HalfW / Mathf.Max(Mathf.Abs(surface.Scale[surface.WAxis]), 1e-6f);
            Vector3 facePoint = surface.Transform.TransformPoint(local);

            var plane = new Plane(normal * side, facePoint);
            float distance;
            if (!plane.Raycast(ray, out distance)) return false;

            worldPoint = ray.GetPoint(distance);

            Vector3 localHit = surface.Transform.InverseTransformPoint(worldPoint) - surface.LocalCenter;
            uv = new Vector2(localHit[surface.UAxis] * surface.Scale[surface.UAxis],
                             localHit[surface.VAxis] * surface.Scale[surface.VAxis]);

            if (!PolygonTriangulator.PointInPolygon(uv, surface.Outer)) return false;

            if (surface.Panel != null)
            {
                foreach (var hole in surface.Panel.Holes)
                {
                    if (hole == null || hole.Points.Count < 3) continue;
                    var scaled = new List<Vector2>(hole.Points.Count);
                    foreach (var p in hole.Points)
                    {
                        scaled.Add(new Vector2(p.x * surface.Scale[surface.UAxis], p.y * surface.Scale[surface.VAxis]));
                    }
                    if (PolygonTriangulator.PointInPolygon(uv, scaled)) return false;
                }
            }

            return true;
        }

        private void StartCutting()
        {
            // Aucune cible n'est requise pour entrer dans le mode : elle se choisit au curseur,
            // dans la vue Scene. L'exiger ici rendait le bouton inopérant.
            _isCuttingActive = true;
            _cutPoints.Clear();
            _cutHover = null;
            _toolBeforeCutting = Tools.current;
            Tools.current = Tool.None;
            SceneView.RepaintAll();
            Repaint();
            Debug.Log("[RoomBuilder] Mode découpe activé : survole la surface à percer dans la vue Scene, " +
                      "puis clique pour poser les points du contour.");
        }

        private void StopCutting(bool restoreTool)
        {
            _isCuttingActive = false;
            _cutHover = null;
            if (restoreTool)
            {
                Tools.current = _toolBeforeCutting;
            }
            SceneView.RepaintAll();
            Repaint();
        }

        private void HandleCutSceneGUI(SceneView sceneView)
        {
            Event e = Event.current;
            int controlID = GUIUtility.GetControlID(FocusType.Passive);

            if (e.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(controlID);
                return;
            }

            // La cible se verrouille au premier point : avant, elle suit le curseur ; après, le
            // contour doit rester sur la même surface. Plus rien à sélectionner au préalable.
            // Le picking de l'éditeur ne se fait qu'aux événements souris : l'appeler à chaque
            // Repaint coûterait cher pour rien, la vue se redessinant en continu.
            if (_cutPoints.Count == 0 &&
                (e.type == EventType.MouseMove || e.type == EventType.MouseDrag || e.type == EventType.MouseDown))
            {
                GameObject picked = HandleUtility.PickGameObject(e.mousePosition, false);
                _cutHover = picked;

                // La cible mémorisée n'est remplacée que par une surface valide : quitter l'objet
                // du curseur ne doit pas vider le panneau, sinon « Retirer le dernier trou »
                // deviendrait inatteignable — il faut sortir de la vue Scene pour y cliquer.
                if (picked != null && IsValidCutTarget(picked, out _)) _cutTarget = picked;
            }

            GameObject candidate = _cutPoints.Count > 0
                ? _cutTarget
                : (IsValidCutTarget(_cutHover, out _) ? _cutHover : null);

            CutSurface surface;
            bool hasSurface = TryDescribeCutSurface(candidate, out surface);

            if (_cutPoints.Count > 0 && !hasSurface)
            {
                // La cible verrouillée a disparu ou changé de nature en cours de tracé.
                _cutPoints.Clear();
                StopCutting(restoreTool: true);
                return;
            }

            Ray mouseRay = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            Vector3 hitPoint = Vector3.zero;
            Vector2 hitUV;
            bool hasHit = hasSurface && TryProjectOnSurface(surface, mouseRay, out hitPoint, out hitUV);

            bool nearFirstPoint = false;
            if (hasHit && _cutPoints.Count >= 3)
            {
                Vector2 firstScreen = HandleUtility.WorldToGUIPoint(_cutPoints[0]);
                nearFirstPoint = Vector2.Distance(firstScreen, e.mousePosition) <= ClosePointScreenDistance;
            }

            if (hasSurface)
            {
                DrawSurfaceOutline(surface, _cutPoints.Count == 0);
                DrawExistingHoles(surface);
            }
            else if (_cutHover != null && _cutPoints.Count == 0)
            {
                DrawRejectedHover(_cutHover);
            }

            // --- Points et segments déjà placés, plus la prévisualisation ---
            Handles.color = Color.cyan;
            for (int i = 0; i < _cutPoints.Count; i++)
            {
                float size = HandleUtility.GetHandleSize(_cutPoints[i]) * 0.08f;
                Handles.SphereHandleCap(0, _cutPoints[i], Quaternion.identity, size, EventType.Repaint);
            }
            if (_cutPoints.Count > 1)
            {
                Handles.DrawAAPolyLine(4f, _cutPoints.ToArray());
            }
            if (hasHit && _cutPoints.Count > 0)
            {
                Handles.color = nearFirstPoint ? Color.green : new Color(0f, 1f, 1f, 0.6f);
                Vector3 previewTarget = nearFirstPoint ? _cutPoints[0] : hitPoint;
                Handles.DrawDottedLine(_cutPoints[_cutPoints.Count - 1], previewTarget, 4f);
            }

            // --- Événements ---
            if (e.type == EventType.MouseMove)
            {
                sceneView.Repaint();
                Repaint();
            }
            else if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                if (hasHit)
                {
                    if (nearFirstPoint)
                    {
                        PerformCut();
                    }
                    else
                    {
                        if (_cutPoints.Count == 0) _cutTarget = candidate;
                        _cutPoints.Add(hitPoint);
                        Repaint();
                        sceneView.Repaint();
                    }
                }
                e.Use();
            }
            else if (e.type == EventType.KeyDown)
            {
                if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && _cutPoints.Count >= 3)
                {
                    PerformCut();
                    e.Use();
                }
                else if (e.keyCode == KeyCode.Escape)
                {
                    _cutPoints.Clear();
                    StopCutting(restoreTool: true);
                    e.Use();
                }
                else if (e.keyCode == KeyCode.Backspace && _cutPoints.Count > 0)
                {
                    _cutPoints.RemoveAt(_cutPoints.Count - 1);
                    Repaint();
                    sceneView.Repaint();
                    e.Use();
                }
            }
        }

        /// <summary>Contour extérieur de la surface visée, sur la face tournée vers la caméra.</summary>
        private static void DrawSurfaceOutline(CutSurface surface, bool hovering)
        {
            Handles.color = hovering ? new Color(0.3f, 1f, 0.6f, 0.85f) : new Color(0.3f, 1f, 0.6f, 0.35f);

            var world = new Vector3[surface.Outer.Count + 1];
            for (int i = 0; i <= surface.Outer.Count; i++)
            {
                world[i] = SurfacePoint(surface, surface.Outer[i % surface.Outer.Count]);
            }
            Handles.DrawAAPolyLine(hovering ? 3f : 2f, world);

            if (hovering)
            {
                Handles.Label(world[0], surface.Target.name);
            }
        }

        /// <summary>Objet survolé mais impossible à découper : on le signale plutôt que de l'ignorer.</summary>
        private static void DrawRejectedHover(GameObject hover)
        {
            var renderer = hover.GetComponent<Renderer>();
            if (renderer == null) return;

            Handles.color = new Color(1f, 0.4f, 0.3f, 0.7f);
            Handles.DrawWireCube(renderer.bounds.center, renderer.bounds.size);
        }

        private static Vector3 SurfacePoint(CutSurface surface, Vector2 uv)
        {
            Vector3 local = surface.LocalCenter;
            local[surface.UAxis] += uv.x / Mathf.Max(Mathf.Abs(surface.Scale[surface.UAxis]), 1e-6f);
            local[surface.VAxis] += uv.y / Mathf.Max(Mathf.Abs(surface.Scale[surface.VAxis]), 1e-6f);
            local[surface.WAxis] += surface.HalfW / Mathf.Max(Mathf.Abs(surface.Scale[surface.WAxis]), 1e-6f);
            return surface.Transform.TransformPoint(local);
        }

        private static void DrawExistingHoles(CutSurface surface)
        {
            RoomPanel panel = surface.Panel;
            if (panel == null || panel.Holes.Count == 0) return;

            Handles.color = new Color(1f, 0.6f, 0.1f, 0.9f);
            foreach (var hole in panel.Holes)
            {
                if (hole == null || hole.Points.Count < 3) continue;

                var world = new Vector3[hole.Points.Count + 1];
                for (int i = 0; i <= hole.Points.Count; i++)
                {
                    Vector2 p = hole.Points[i % hole.Points.Count];
                    world[i] = SurfacePoint(surface, new Vector2(p.x * surface.Scale[surface.UAxis],
                                                                 p.y * surface.Scale[surface.VAxis]));
                }
                Handles.DrawAAPolyLine(2f, world);
            }
        }

        private void PerformCut()
        {
            if (!IsValidCutTarget(_cutTarget, out string reason))
            {
                Debug.LogWarning($"[RoomBuilder] Découpe annulée : {reason}");
                return;
            }
            if (_cutPoints.Count < 3)
            {
                Debug.LogWarning("[RoomBuilder] Découpe annulée : il faut au moins 3 points.");
                return;
            }

            CutSurface surface;
            if (!TryDescribeCutSurface(_cutTarget, out surface))
            {
                Debug.LogWarning("[RoomBuilder] Découpe annulée : la cible n'est plus exploitable.");
                return;
            }

            Transform targetTransform = surface.Transform;
            RoomPanel existing = surface.Panel;
            Vector3 scale = surface.Scale;
            Vector3 panelSize = surface.PanelSize;
            int uAxis = surface.UAxis, vAxis = surface.VAxis, wAxis = surface.WAxis;
            List<Vector2> outerContour = surface.Outer;

            // Contour du trou en coordonnées (U,V) du panneau, en unités monde.
            // InverseTransformPoint renvoie des coordonnées en espace MESH ; les remultiplier par
            // l'échelle du transform donne bien une position en unités monde, cohérente avec le
            // contour extérieur (sans cela le trou est « scale » fois trop petit et ramené au centre).
            var holePoints = new List<Vector2>(_cutPoints.Count);
            foreach (var worldPoint in _cutPoints)
            {
                Vector3 local = targetTransform.InverseTransformPoint(worldPoint) - surface.LocalCenter;
                holePoints.Add(new Vector2(local[uAxis] * scale[uAxis], local[vAxis] * scale[vAxis]));
            }

            if (!PolygonTriangulator.IsSimplePolygon(holePoints))
            {
                Debug.LogWarning("[RoomBuilder] Le contour se recoupe lui-même (ou contient deux points confondus) — découpe annulée. Redessine un contour simple.");
                return;
            }

            const float margin = 0.001f;
            if (!PolygonTriangulator.ContainsWithMargin(outerContour, holePoints, margin))
            {
                Debug.LogWarning("[RoomBuilder] Le contour du trou sort de la cible ou en touche le bord — découpe annulée. Redessine-le entièrement à l'intérieur.");
                return;
            }

            // Trous déjà percés, réexprimés dans la nouvelle échelle (identité si l'objet n'a pas
            // été redimensionné depuis la découpe précédente).
            var previousHoles = new List<List<Vector2>>();
            if (existing != null)
            {
                foreach (var hole in existing.Holes)
                {
                    if (hole == null || hole.Points.Count < 3) continue;
                    var scaled = new List<Vector2>(hole.Points.Count);
                    foreach (var p in hole.Points)
                    {
                        scaled.Add(new Vector2(p.x * scale[uAxis], p.y * scale[vAxis]));
                    }
                    previousHoles.Add(scaled);
                }
            }

            foreach (var previous in previousHoles)
            {
                if (PolygonTriangulator.PolygonsOverlap(holePoints, previous))
                {
                    Debug.LogWarning("[RoomBuilder] Ce contour chevauche un trou déjà percé — découpe annulée. Dessine une ouverture séparée, ou retire d'abord le trou existant.");
                    return;
                }
            }

            Undo.SetCurrentGroupName($"Cut Hole in {_cutTarget.name}");
            int undoGroup = Undo.GetCurrentGroup();

            RoomPanel panel = existing;
            if (panel == null)
            {
                panel = Undo.AddComponent<RoomPanel>(_cutTarget);
            }
            else
            {
                Undo.RecordObject(panel, "Cut Hole");
            }

            panel.Size = panelSize;
            panel.Outer = new List<Vector2>(outerContour);
            panel.UAxis = uAxis;
            panel.VAxis = vAxis;
            panel.WAxis = wAxis;
            panel.Holes.Clear();
            foreach (var previous in previousHoles) panel.Holes.Add(new RoomPanel.Hole(previous));
            panel.Holes.Add(new RoomPanel.Hole(holePoints));
            EditorUtility.SetDirty(panel);

            // Le mesh généré est déjà à la taille réelle : laisser l'ancien localScale en place
            // ferait multiplier les dimensions une seconde fois par Unity au rendu (mur qui
            // « grossit » et devient invisible de l'intérieur).
            Undo.RecordObject(targetTransform, "Normalize Cut Target");

            // Le mesh généré est centré sur le pivot. Si l'ancien ne l'était pas, on déplace le
            // pivot d'autant pour que la géométrie ne bouge pas d'un pouce à l'écran.
            if (surface.LocalCenter.sqrMagnitude > 1e-12f)
            {
                targetTransform.localPosition += targetTransform.localRotation
                    * Vector3.Scale(surface.LocalCenter, targetTransform.localScale);
            }

            // Le mesh généré est déjà à la taille réelle : laisser l'ancien localScale en place
            // ferait multiplier les dimensions une seconde fois par Unity au rendu.
            targetTransform.localScale = Vector3.one;

            if (!RebuildPanelMesh(_cutTarget, panel, registerUndo: true))
            {
                Debug.LogWarning("[RoomBuilder] Reconstruction du panneau impossible — découpe annulée.");
                Undo.RevertAllDownToGroup(undoGroup);
                return;
            }

            Undo.CollapseUndoOperations(undoGroup);

            Debug.Log($"[RoomBuilder] Trou découpé dans '{_cutTarget.name}' ({holePoints.Count} points) — {panel.Holes.Count} trou(s) au total.");

            // On reste en mode découpe : le level designer peut enchaîner porte, fenêtres, etc.
            _cutPoints.Clear();
            SceneView.RepaintAll();
            Repaint();
        }

        private void RemoveLastHole()
        {
            if (_cutTarget == null) return;
            var panel = _cutTarget.GetComponent<RoomPanel>();
            if (panel == null || panel.Holes.Count == 0) return;

            Undo.SetCurrentGroupName($"Remove Hole from {_cutTarget.name}");
            int undoGroup = Undo.GetCurrentGroup();

            Undo.RecordObject(panel, "Remove Hole");
            panel.Holes.RemoveAt(panel.Holes.Count - 1);
            EditorUtility.SetDirty(panel);

            RebuildPanelMesh(_cutTarget, panel, registerUndo: true);
            Undo.CollapseUndoOperations(undoGroup);

            SceneView.RepaintAll();
            Repaint();
        }

        /// <summary>
        /// Le contenu d'un asset mesh n'est pas couvert par le Ctrl+Z : après un undo, le composant
        /// RoomPanel retrouve son ancienne liste de trous mais le mesh, lui, garde la géométrie de
        /// la dernière découpe. On le régénère donc à partir de l'état restauré du composant, qui
        /// fait seul autorité.
        /// </summary>
        private void OnUndoRedoPerformed()
        {
            if (_cutTarget == null) return;
            var panel = _cutTarget.GetComponent<RoomPanel>();
            if (panel != null)
            {
                RebuildPanelMesh(_cutTarget, panel, registerUndo: false);
            }
            SceneView.RepaintAll();
            Repaint();
        }

        /// <summary>
        /// Régénère intégralement le mesh du panneau à partir de son état logique (dimensions +
        /// liste des trous). C'est ce qui rend la découpe répétable : chaque découpe reconstruit
        /// le panneau complet au lieu de tenter de retoucher le mesh précédent.
        /// </summary>
        private static bool RebuildPanelMesh(GameObject target, RoomPanel panel, bool registerUndo)
        {
            var holeLoops = new List<List<Vector2>>(panel.Holes.Count);
            foreach (var hole in panel.Holes)
            {
                if (hole != null && hole.Points.Count >= 3) holeLoops.Add(hole.Points);
            }

            // Toute la géométrie est assemblée par PanelMeshBuilder, qui ne dépend d'aucune API de
            // l'éditeur et se teste donc hors d'Unity (voir Tools/GeometryTests). Ce qui reste ici
            // est ce qu'Unity seul peut faire : écrire l'asset, recuire le collider, inscrire
            // l'opération dans l'undo.
            PanelMesh built = PanelMeshBuilder.Build(panel.ResolveOuterContour(), panel.Thickness,
                panel.UAxis, panel.VAxis, panel.WAxis, holeLoops);
            if (built == null)
            {
                Debug.LogError($"[RoomBuilder] Surface vide pour '{target.name}' : géométrie du panneau incohérente.");
                return false;
            }

            var meshFilter = target.GetComponent<MeshFilter>();
            if (meshFilter == null)
            {
                meshFilter = registerUndo ? Undo.AddComponent<MeshFilter>(target) : target.AddComponent<MeshFilter>();
            }

            Mesh mesh = GetWritablePanelMesh(meshFilter.sharedMesh, PanelAssetName(target));
            mesh.Clear();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(built.Vertices);
            mesh.SetUVs(0, built.UVs);
            mesh.SetTriangles(built.Triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            EditorUtility.SetDirty(mesh);
            AssetDatabase.SaveAssets();

            if (registerUndo) Undo.RecordObject(meshFilter, "Panel Mesh");
            meshFilter.sharedMesh = mesh;

            var boxCollider = target.GetComponent<BoxCollider>();
            if (boxCollider != null)
            {
                // Ne correspond plus à la forme une fois le panneau troué.
                if (registerUndo) Undo.DestroyObjectImmediate(boxCollider);
                else DestroyImmediate(boxCollider);
            }

            var meshCollider = target.GetComponent<MeshCollider>();
            if (meshCollider == null)
            {
                meshCollider = registerUndo ? Undo.AddComponent<MeshCollider>(target) : target.AddComponent<MeshCollider>();
            }
            else if (registerUndo)
            {
                Undo.RecordObject(meshCollider, "Panel Collider");
            }

            // L'asset mesh étant réécrit en place, sa référence ne change pas : sans ce passage par
            // null le MeshCollider conserverait la collision cuite pour la géométrie précédente,
            // et les clics de la découpe suivante tomberaient sur l'ancienne forme.
            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = mesh;

            return true;
        }

        /// <summary>Contour rectangulaire d'un panneau, a partir de ses dimensions.</summary>
        private static List<Vector2> RectangleContour(Vector3 size, int uAxis, int vAxis)
        {
            float hu = Mathf.Abs(size[uAxis]) * 0.5f;
            float hv = Mathf.Abs(size[vAxis]) * 0.5f;
            return new List<Vector2>
            {
                new Vector2(-hu, -hv),
                new Vector2(hu, -hv),
                new Vector2(hu, hv),
                new Vector2(-hu, hv),
            };
        }

        private static string PanelAssetName(GameObject target)
        {
            string prefix = target.transform.parent != null ? target.transform.parent.name + "_" : string.Empty;
            return $"{prefix}{target.name}_Panel";
        }

        /// <summary>
        /// Renvoie le mesh à (ré)écrire. Un panneau déjà généré voit son asset réécrit en place :
        /// sans cela chaque découpe créerait un nouvel asset (Wall_Cut, Wall_Cut_Cut…) et laisserait
        /// le précédent orphelin dans le projet. Un asset partagé par plusieurs objets (mur dupliqué
        /// avec Ctrl+D) est en revanche laissé intact et remplacé par une copie, pour ne pas percer
        /// un trou dans tous les jumeaux à la fois.
        /// </summary>
        private static Mesh GetWritablePanelMesh(Mesh current, string baseName)
        {
            if (current != null)
            {
                string path = AssetDatabase.GetAssetPath(current);
                if (!string.IsNullOrEmpty(path)
                    && path.StartsWith(GeneratedMeshFolder + "/")
                    && CountSceneUsers(current) <= 1)
                {
                    return current;
                }
            }
            return SaveMeshAsset(new Mesh(), baseName);
        }

        private static int CountSceneUsers(Mesh mesh)
        {
            int count = 0;
            foreach (var meshFilter in FindObjectsByType<MeshFilter>(FindObjectsInactive.Include))
            {
                if (meshFilter.sharedMesh == mesh) count++;
            }
            return count;
        }

        private void GenerateFreehandRoom()
        {
            if (!IsFreehandConfigValid())
            {
                Debug.LogWarning("[RoomBuilder] Contour invalide (min. 3 points, hauteur et épaisseur > 0).");
                return;
            }

            var points = new List<Vector3>(_fpPoints);

            // Centroïde (moyenne des sommets) utilisé comme pivot de la salle générée.
            Vector3 centroid = Vector3.zero;
            foreach (var p in points) centroid += p;
            centroid /= points.Count;
            centroid.y = _fpGroundY;

            float sign = _fpDirection == ExtrudeDirection.Up ? 1f : -1f;
            float h = _fpHeight * sign;
            float t = _fpWallThickness;

            Transform root = GetOrCreateRootFolder();
            string uniqueName = GameObjectUtility.GetUniqueNameForSibling(root, string.IsNullOrEmpty(_fpRoomName) ? "Room" : _fpRoomName);

            Undo.SetCurrentGroupName($"Generate Freehand Room {uniqueName}");
            int undoGroup = Undo.GetCurrentGroup();

            var roomRoot = new GameObject(uniqueName);
            Undo.RegisterCreatedObjectUndo(roomRoot, "Create Room Root");
            roomRoot.transform.SetParent(root, worldPositionStays: true);
            roomRoot.transform.position = centroid;
            roomRoot.transform.rotation = Quaternion.identity;

            // Points locaux (XZ) relatifs au centroïde, dans le plan de dessin.
            var localPoints2D = new List<Vector2>(points.Count);
            foreach (var p in points)
            {
                localPoints2D.Add(new Vector2(p.x - centroid.x, p.z - centroid.z));
            }

            if (_fpAddFloor || _fpAddCeiling)
            {
                Material defaultMaterial = GetDefaultPrimitiveMaterial();

                if (_fpAddFloor)
                {
                    // Sol au niveau du plan de dessin (Y local 0). Généré double-face
                    // (visible du dessus ET du dessous) pour ne jamais dépendre du sens
                    // d'orientation du contour dessiné par l'utilisateur.
                    // Face supérieure au niveau du plan de dessin : on marche sur Y = 0.
                    CreateSlabPanel(roomRoot.transform, "Floor", localPoints2D, -t * 0.5f, t, defaultMaterial);
                }

                if (_fpAddCeiling)
                {
                    // Plafond au sommet de l'extrusion, également généré double-face.
                    // Face inférieure au sommet de l'extrusion.
                    CreateSlabPanel(roomRoot.transform, "Ceiling", localPoints2D, h + t * 0.5f, t, defaultMaterial);
                }
            }

            int count = points.Count;
            for (int i = 0; i < count; i++)
            {
                Vector3 a = points[i];
                Vector3 b = points[(i + 1) % count];

                Vector3 edge = b - a;
                float length = new Vector2(edge.x, edge.z).magnitude;
                if (length < 0.001f) continue; // deux points confondus : segment ignoré

                Vector3 direction = new Vector3(edge.x, 0f, edge.z).normalized;
                Vector3 midWorld = (a + b) * 0.5f;
                Vector3 localMid = new Vector3(midWorld.x - centroid.x, h * 0.5f, midWorld.z - centroid.z);

                Quaternion rotation = Quaternion.FromToRotation(Vector3.right, direction);

                CreateBoxPart(roomRoot.transform, $"Wall_{i:00}", localMid, new Vector3(length, Mathf.Abs(h), t), rotation);
            }

            Undo.CollapseUndoOperations(undoGroup);

            Selection.activeGameObject = roomRoot;
            EditorGUIUtility.PingObject(roomRoot);

            Debug.Log($"[RoomBuilder] Salle (contour libre) '{uniqueName}' générée avec {count} points, " +
                      $"hauteur {h:F2}, direction {_fpDirection}.");

            _fpPoints.Clear();
            StopDrawing(restoreTool: true);
        }

        private static Material GetDefaultPrimitiveMaterial()
        {
            // Réutilise le même matériau par défaut que celui assigné automatiquement
            // par GameObject.CreatePrimitive, pour rester visuellement cohérent avec le mode Boîte.
            GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Material mat = temp.GetComponent<Renderer>().sharedMaterial;
            DestroyImmediate(temp);
            return mat;
        }

        /// <summary>
        /// Crée un sol ou un plafond de contour libre. C'est un panneau au même titre qu'un mur :
        /// un contour plat extrudé sur une épaisseur, porteur d'un RoomPanel.
        ///
        /// Ils étaient auparavant de simples surfaces sans épaisseur, générées par un chemin de
        /// code distinct — ce qui les rendait indécoupables (l'outil l'annonçait dans son aide) et
        /// les faisait disparaître vus par la tranche. Les unifier supprime les deux défauts d'un
        /// coup, et tout ce qui vaut pour un mur vaut désormais pour eux.
        /// </summary>
        private static void CreateSlabPanel(Transform parent, string name, List<Vector2> localPoints2D,
            float localY, float thickness, Material material)
        {
            const int uAxis = 0;   // X
            const int vAxis = 2;   // Z
            const int wAxis = 1;   // Y : l'épaisseur d'une dalle

            PanelMesh built = PanelMeshBuilder.Build(localPoints2D, thickness, uAxis, vAxis, wAxis, null);
            if (built == null)
            {
                Debug.LogWarning($"[RoomBuilder] Triangulation impossible pour '{name}' (contour invalide ou auto-intersectant) — élément ignoré.");
                return;
            }

            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = new Vector3(0f, localY, 0f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var p in localPoints2D)
            {
                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
                if (p.y < minZ) minZ = p.y;
                if (p.y > maxZ) maxZ = p.y;
            }

            var panel = go.AddComponent<RoomPanel>();
            panel.Size = new Vector3(maxX - minX, thickness, maxZ - minZ);
            panel.UAxis = uAxis;
            panel.VAxis = vAxis;
            panel.WAxis = wAxis;
            panel.Outer = new List<Vector2>(localPoints2D);

            Mesh mesh = SaveMeshAsset(new Mesh(), $"{parent.name}_{name}");
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(built.Vertices);
            mesh.SetUVs(0, built.UVs);
            mesh.SetTriangles(built.Triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            EditorUtility.SetDirty(mesh);
            AssetDatabase.SaveAssets();

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;

            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI
                | StaticEditorFlags.OccluderStatic
                | StaticEditorFlags.OccludeeStatic
                | StaticEditorFlags.BatchingStatic
                | StaticEditorFlags.NavigationStatic
                | StaticEditorFlags.ReflectionProbeStatic);
        }

        // ===================== UTILITAIRES COMMUNS =====================

        /// <summary>
        /// Enregistre le mesh généré comme asset sur disque et renvoie l'instance persistée.
        /// Un Mesh créé à la volée (new Mesh()) puis affecté à un objet de scène n'est PAS
        /// sérialisé avec la scène : il survit à la session en cours, mais à la réouverture du
        /// projet le MeshFilter pointe dans le vide et la géométrie disparaît. Cela concerne
        /// aussi bien les murs découpés que les sols/plafonds du mode Dessin libre.
        /// Note : la création d'asset n'est pas couverte par le Ctrl+Z — annuler une découpe
        /// laisse le fichier .asset en place, inutilisé.
        /// </summary>
        private static Mesh SaveMeshAsset(Mesh mesh, string baseName)
        {
            const string parentFolder = "Assets";
            const string folderName = "GeneratedMeshes";
            const string folder = GeneratedMeshFolder;

            if (!AssetDatabase.IsValidFolder(folder))
            {
                AssetDatabase.CreateFolder(parentFolder, folderName);
            }

            char[] invalid = System.IO.Path.GetInvalidFileNameChars();
            var safe = new System.Text.StringBuilder(baseName.Length);
            foreach (char ch in baseName)
            {
                safe.Append(System.Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
            }

            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{safe}.asset");
            AssetDatabase.CreateAsset(mesh, path);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<Mesh>(path);
        }

        private Transform GetOrCreateRootFolder()
        {
            var existing = GameObject.Find(RootFolderName);
            if (existing != null)
            {
                return existing.transform;
            }

            var folder = new GameObject(RootFolderName);
            Undo.RegisterCreatedObjectUndo(folder, "Create Rooms Folder");
            return folder.transform;
        }

        private static void SnapPositionToSceneView(ref Vector3 position)
        {
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
            {
                Debug.LogWarning("[RoomBuilder] Aucune vue Scene active à utiliser comme référence.");
                return;
            }
            Vector3 pivot = sceneView.pivot;
            position = new Vector3(pivot.x, pivot.y, pivot.z);
        }

        private static void CreateBoxPart(Transform parent, string name, Vector3 localPosition, Vector3 size)
        {
            CreateBoxPart(parent, name, localPosition, size, Quaternion.identity);
        }

        private static void CreateBoxPart(Transform parent, string name, Vector3 localPosition, Vector3 size, Quaternion localRotation)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Undo.RegisterCreatedObjectUndo(go, $"Create {name}");

            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            go.transform.localScale = size;

            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI
                | StaticEditorFlags.OccluderStatic
                | StaticEditorFlags.OccludeeStatic
                | StaticEditorFlags.BatchingStatic
                | StaticEditorFlags.NavigationStatic
                | StaticEditorFlags.OffMeshLinkGeneration
                | StaticEditorFlags.ReflectionProbeStatic);
        }
    }
}
