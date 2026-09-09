using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace KingdomWatch.Prototypes
{
    // Disposable presentation experiment. No simulation or Core dependencies.
    public sealed class TownPrototype : MonoBehaviour
    {
        public Shader spriteShader;
        public Shader geometryShader;
        public Camera sceneCamera;
        private Camera view;
        private Transform town;
        private readonly List<Transform> people = new List<Transform>();
        private readonly List<Vector2> homes = new List<Vector2>();
        private readonly List<Object> owned = new List<Object>();
        private readonly List<Transform> selectable = new List<Transform>();
        private Sprite personSprite, treeSprite;
        private Material spriteMaterial, blockMaterial;
        private Mesh roofMesh;
        private Light sunlight;
        private readonly List<Transform> billboards = new List<Transform>();
        private float yaw, tilt = 50, sunAngle = 325;
        private bool animateSun = true;
        private Transform selected;
        private Vector2 focus, previousFocus, pointerStart, pointerLast;
        private float previousZoom = 19, pinchDistance;
        private bool dragging, moved, following, blocked;
        private float elapsed;
        private int cycle;
        private Vector2 lastTap;
        private float lastTapTime = -10;
        private float UiScale => Mathf.Max(1, Screen.height / 720f);

        private void OnEnable() => EnhancedTouchSupport.Enable();
        private void OnDisable() => EnhancedTouchSupport.Disable();

        private void Start()
        {
            view = sceneCamera != null ? sceneCamera : new GameObject("Prototype Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            view.tag = "MainCamera";
            view.orthographic = true;
            view.orthographicSize = 19;
            view.nearClipPlane = .1f;
            view.farClipPlane = 200;
            view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = new Color(.13f, .19f, .2f);
            UpdateCamera();
            spriteMaterial = Own(new Material(spriteShader));
            blockMaterial = Own(new Material(geometryShader));
            blockMaterial.SetFloat("_Smoothness", .08f);
            roofMesh = CreateRoofMesh();
            personSprite = PixelSprite(new[] { "...hhh...", "..hhhhh..", "..hsssh..", "...sss...", "..ttttt..", ".sttttts.", ".sttttts.", "..ttttt..", "..bb.bb..", "..bb.bb..", "..kk.kk.." });
            treeSprite = PixelSprite(new[] { ".....gg.....", "....gggg....", "...gggggg...", "..gggggggg..", ".gggggggggg.", "gggggggggggg", "..gggggggg..", "....dd......", "....dd......" });
            BuildTown();
        }

        private T Own<T>(T value) where T : Object { owned.Add(value); return value; }

        private Sprite PixelSprite(string[] rows)
        {
            int width = 0;
            foreach (string row in rows) width = Mathf.Max(width, row.Length);
            var texture = Own(new Texture2D(width, rows.Length, TextureFormat.RGBA32, false));
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color[width * rows.Length];
            for (int y = 0; y < rows.Length; y++)
                for (int x = 0; x < rows[y].Length; x++)
                    pixels[(rows.Length - 1 - y) * width + x] = Ink(rows[y][x]);
            texture.SetPixels(pixels);
            texture.Apply();
            return Own(Sprite.Create(texture, new Rect(0, 0, width, rows.Length), new Vector2(.5f, 0), width));
        }

        private static Color Ink(char c)
        {
            switch (c)
            {
                case 'h': return new Color(.22f, .15f, .12f);
                case 's': return new Color(.91f, .69f, .46f);
                case 't': return new Color(.47f, .73f, .81f);
                case 'b': return new Color(.27f, .31f, .4f);
                case 'k': return new Color(.12f, .16f, .2f);
                case 'r': return new Color(.65f, .29f, .2f);
                case 'w': return new Color(.9f, .83f, .65f);
                case 'd': return new Color(.35f, .23f, .16f);
                case 'g': return new Color(.22f, .43f, .24f);
                default: return Color.clear;
            }
        }

        private Vector3 Position(Vector2 p, float height = 0) => new Vector3(p.x, height, p.y);

        private Transform SpriteObject(string label, Sprite sprite, Vector2 p, float size, Color tint)
        {
            var go = new GameObject(label, typeof(SpriteRenderer));
            go.transform.SetParent(town);
            go.transform.position = Position(p, .05f);
            go.transform.localScale = Vector3.one * size;
            go.transform.rotation = view.transform.rotation;
            billboards.Add(go.transform);
            var renderer = go.GetComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sharedMaterial = spriteMaterial;
            renderer.color = tint;
            return go.transform;
        }

        private void Tile(string label, Vector2 p, Vector2 size, Color tint)
        {
            // Opaque ground receives shadows and participates in depth testing.
            float top = label == "Meadow" ? 0 : label == "River" ? .045f : label == "Market" ? .035f : .02f;
            Block(label, p, new Vector3(size.x, .05f, size.y), tint, top - .05f);
        }

        private Transform Block(string label, Vector2 p, Vector3 size, Color color, float baseHeight = 0)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = label;
            go.transform.SetParent(town);
            go.transform.position = Position(p, baseHeight + size.y / 2);
            go.transform.localScale = size;
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = blockMaterial;
            var properties = new MaterialPropertyBlock();
            properties.SetColor("_BaseColor", color);
            renderer.SetPropertyBlock(properties);
            return go.transform;
        }

        private Mesh CreateRoofMesh()
        {
            // Separate vertices per face retain crisp gables and roof planes.
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            void Face(params Vector3[] points)
            {
                int start = vertices.Count;
                vertices.AddRange(points);
                for (int i = 1; i < points.Length - 1; i++)
                { triangles.Add(start); triangles.Add(start + i); triangles.Add(start + i + 1); }
            }
            Vector3 lf = new Vector3(-2, 0, -1.8f), rf = new Vector3(2, 0, -1.8f);
            Vector3 lb = new Vector3(-2, 0, 1.8f), rb = new Vector3(2, 0, 1.8f);
            Vector3 peakF = new Vector3(0, 1.4f, -1.8f), peakB = new Vector3(0, 1.4f, 1.8f);
            Face(lf, peakF, rf);
            Face(lb, rb, peakB);
            Face(lf, lb, peakB, peakF);
            Face(rf, peakF, peakB, rb);
            Face(lf, rf, rb, lb);
            var mesh = Own(new Mesh { name = "Shared pitched roof" });
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private Transform House(Vector2 p, int index)
        {
            var plaster = new Color(.82f, .72f, .52f);
            var timber = new Color(.28f, .17f, .10f);
            var glass = new Color(.12f, .23f, .27f);
            var house = Block("House " + (index + 1), p, new Vector3(3.5f, 2.5f, 3), plaster);
            var roof = new GameObject("Pitched roof", typeof(MeshFilter), typeof(MeshRenderer));
            roof.transform.SetParent(town);
            roof.transform.position = Position(p, 2.5f);
            roof.GetComponent<MeshFilter>().sharedMesh = roofMesh;
            var renderer = roof.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = blockMaterial;
            var properties = new MaterialPropertyBlock();
            properties.SetColor("_BaseColor", new Color(.62f, .25f, .14f));
            renderer.SetPropertyBlock(properties);

            // Front faces the camera (negative Z). Small offsets avoid coplanar surfaces.
            Block("Stone foundation", p, new Vector3(3.65f, .18f, 3.15f), new Color(.4f, .39f, .34f));
            Block("Door frame", p + new Vector2(0, -1.53f), new Vector3(.88f, 1.55f, .12f), timber, .18f);
            Block("Door", p + new Vector2(0, -1.61f), new Vector3(.65f, 1.34f, .06f), new Color(.48f, .3f, .15f), .18f);
            Block("Doorstep", p + new Vector2(0, -1.82f), new Vector3(1.05f, .15f, .6f), new Color(.52f, .51f, .45f));
            foreach (float x in new[] { -1.1f, 1.1f })
            {
                var window = p + new Vector2(x, -1.55f);
                Block("Window frame", window, new Vector3(.72f, .83f, .12f), timber, 1.02f);
                Block("Window glass", window + new Vector2(0, -.075f), new Vector3(.54f, .63f, .05f), glass, 1.12f);
                Block("Window mullion", window + new Vector2(0, -.11f), new Vector3(.06f, .63f, .04f), plaster, 1.12f);
                Block("Window crossbar", window + new Vector2(0, -.11f), new Vector3(.54f, .06f, .04f), plaster, 1.4f);
            }
            foreach (float x in new[] { -1.68f, 1.68f })
                Block("Corner timber", p + new Vector2(x, -1.53f), new Vector3(.13f, 2.32f, .12f), timber, .18f);
            Block("Eaves beam", p + new Vector2(0, -1.54f), new Vector3(3.5f, .15f, .12f), timber, 2.32f);
            Block("Chimney", p + new Vector2(.95f, .55f), new Vector3(.48f, 1.3f, .5f), new Color(.44f, .39f, .33f), 3.0f);
            return house;
        }

        private void AddDaylight()
        {
            var sun = new GameObject("Afternoon sun", typeof(Light)).GetComponent<Light>();
            sun.transform.SetParent(town);
            sun.transform.rotation = Quaternion.Euler(48, -35, 0);
            sun.type = LightType.Directional;
            sun.color = new Color(1, .94f, .83f);
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = .65f;
            sunlight = sun;
            var fill = new GameObject("Sky fill", typeof(Light)).GetComponent<Light>();
            fill.transform.SetParent(town);
            fill.transform.rotation = Quaternion.Euler(65, 145, 0);
            fill.type = LightType.Directional;
            fill.color = new Color(.7f, .82f, 1);
            fill.intensity = .45f;
            fill.shadows = LightShadows.None;
        }

        private void BuildBridge()
        {
            Tile("River", new Vector2(23, 0), new Vector2(4, 49), new Color(.22f, .48f, .58f));
            var wood = new Color(.48f, .29f, .14f);
            Block("Raised bridge deck", new Vector2(23, 0), new Vector3(9, .25f, 2.5f), wood, 1.9f);
            foreach (float x in new[] { 19.5f, 23, 26.5f })
                foreach (float z in new[] { -1.1f, 1.1f })
                {
                    Block("Bridge pier", new Vector2(x, z), new Vector3(.35f, 1.9f, .35f), new Color(.4f, .4f, .36f));
                    Block("Railing post", new Vector2(x, z), new Vector3(.14f, .9f, .14f), wood, 2.15f);
                }
            foreach (float z in new[] { -1.1f, 1.1f })
                Block("Bridge handrail", new Vector2(23, z), new Vector3(9, .13f, .14f), wood, 2.9f);
            for (int step = 0; step < 6; step++)
                foreach (int side in new[] { -1, 1 })
                    Block("Bridge steps", new Vector2(23 + side * (4.75f + step * .45f), 0),
                        new Vector3(.46f, (6 - step) * 2.15f / 6, 2.5f), wood);
        }

        private void BuildTown()
        {
            if (town != null) { town.gameObject.SetActive(false); Destroy(town.gameObject); }
            people.Clear(); homes.Clear(); selectable.Clear(); selected = null; following = false;
            billboards.Clear(); sunlight = null;
            town = new GameObject("Seeded town - prototype only").transform;
            UpdateCamera();
            AddDaylight();
            Tile("Meadow", Vector2.zero, new Vector2(65, 55), new Color(.49f, .59f, .35f));
            Tile("East west road", Vector2.zero, new Vector2(57, 2), new Color(.68f, .59f, .43f));
            Tile("North south road", Vector2.zero, new Vector2(2, 47), new Color(.68f, .59f, .43f));
            Tile("Market", Vector2.zero, new Vector2(7, 7), new Color(.74f, .66f, .51f));
            BuildBridge();
            var rng = new System.Random(731);
            for (int i = 0; i < 16; i++)
            {
                var p = new Vector2((i % 4 - 1.5f) * 9, (i / 4 - 1.5f) * 8);
                selectable.Add(House(p, i));
            }
            for (int i = 0; i < 45; i++)
            {
                var p = new Vector2((float)rng.NextDouble() * 58 - 29, (float)rng.NextDouble() * 48 - 24);
                if (Mathf.Abs(p.x) < 20 && Mathf.Abs(p.y) < 17) continue;
                SpriteObject("Tree", treeSprite, p, 3, Color.white);
            }
            for (int i = 0; i < 80; i++)
            {
                // Twenty market villagers intentionally overlap to test repeated-tap cycling.
                var p = i < 20 ? new Vector2((float)rng.NextDouble() * 3 - 1.5f, (float)rng.NextDouble() * 2 - 1) :
                    new Vector2((float)rng.NextDouble() * 42 - 21, (i % 2 == 0 ? -1 : 1) * 1.6f);
                homes.Add(p);
                var person = SpriteObject((i % 2 == 0 ? "Human " : "Elf ") + (i + 1), personSprite, p, .8f,
                    i % 2 == 0 ? Color.white : new Color(.8f, 1, .76f));
                people.Add(person); selectable.Add(person);
            }
        }

        private void Update()
        {
            if (view == null) return;
            elapsed += Time.deltaTime;
            for (int i = 0; i < people.Count; i++)
            {
                Vector2 p = homes[i] + new Vector2(Mathf.Sin(elapsed * .35f + i) * (i < 20 ? .3f : 2), Mathf.Cos(elapsed * .3f + i) * .22f);
                if (i == people.Count - 1) p = new Vector2(23 + Mathf.Sin(elapsed * .4f) * 3.5f, 0);
                people[i].position = Position(p, i == people.Count - 1 ? 2.16f : .06f);
            }
            ReadPointer();
            if (following && selected != null) focus = MapPosition(selected.position);
            UpdateCamera();
            foreach (Transform billboard in billboards) billboard.rotation = view.transform.rotation;
            if (sunlight != null)
            {
                if (animateSun) sunAngle = Mathf.Repeat(sunAngle + Time.deltaTime * 8, 360);
                sunlight.transform.rotation = Quaternion.Euler(35 + 20 * Mathf.Sin(sunAngle * Mathf.Deg2Rad), sunAngle, 0);
            }
        }

        private Vector2 MapPosition(Vector3 p) => new Vector2(p.x, p.z);
        private void UpdateCamera()
        {
            view.transform.rotation = Quaternion.Euler(tilt, yaw, 0);
            view.transform.position = Position(focus) - view.transform.forward * 70;
        }

        private bool InPanel(Vector2 p) => p.x < 430 * UiScale && p.y > Screen.height - 470 * UiScale;
        private void ReadPointer()
        {
            var touches = Touch.activeTouches;
            if (touches.Count >= 2)
            {
                float distance = Vector2.Distance(touches[0].screenPosition, touches[1].screenPosition);
                if (pinchDistance > 0 && distance > 0) view.orthographicSize = Mathf.Clamp(view.orthographicSize * pinchDistance / distance, 3, 45);
                pinchDistance = distance; dragging = false; moved = true; return;
            }
            if (pinchDistance > 0) { pinchDistance = 0; blocked = true; }
            Vector2 p;
            bool down;
            if (touches.Count == 1) { p = touches[0].screenPosition; down = touches[0].phase != UnityEngine.InputSystem.TouchPhase.Ended && touches[0].phase != UnityEngine.InputSystem.TouchPhase.Canceled; }
            else if (Mouse.current != null)
            {
                p = Mouse.current.position.ReadValue(); down = Mouse.current.leftButton.isPressed;
                if (!InPanel(p)) view.orthographicSize = Mathf.Clamp(view.orthographicSize * Mathf.Exp(-Mouse.current.scroll.ReadValue().y * .001f), 3, 45);
            }
            else { p = pointerLast; down = false; }
            if (blocked) { if (!down) blocked = false; return; }
            if (down && !dragging)
            {
                if (InPanel(p)) { blocked = true; return; }
                dragging = true; moved = false; pointerStart = pointerLast = p;
            }
            if (down && dragging)
            {
                if (Vector2.Distance(pointerStart, p) > 10 * UiScale) moved = true;
                if (moved)
                {
                    // Project onto the ground so dragging remains correct at every orbit angle.
                    var ground = new Plane(Vector3.up, Vector3.zero);
                    Ray oldRay = view.ScreenPointToRay(pointerLast), newRay = view.ScreenPointToRay(p);
                    if (ground.Raycast(oldRay, out float oldDistance) && ground.Raycast(newRay, out float newDistance))
                        focus += MapPosition(oldRay.GetPoint(oldDistance) - newRay.GetPoint(newDistance));
                    following = false;
                }
                pointerLast = p;
            }
            if (!down && dragging) { dragging = false; if (!moved) Select(pointerLast); }
        }

        private void Select(Vector2 p)
        {
            var candidates = new List<Transform>();
            foreach (Transform target in selectable)
            {
                var renderer = target.GetComponent<Renderer>();
                Vector3 screen = view.WorldToScreenPoint(renderer.bounds.center);
                float radius = people.Contains(target) ? 22 * UiScale : Mathf.Clamp(2 * Screen.height / (2 * view.orthographicSize), 16 * UiScale, 65 * UiScale);
                if (screen.z > 0 && Vector2.Distance(p, screen) < radius) candidates.Add(target);
            }
            candidates.Sort((a, b) => Vector2.Distance(p, view.WorldToScreenPoint(a.GetComponent<Renderer>().bounds.center)).CompareTo(Vector2.Distance(p, view.WorldToScreenPoint(b.GetComponent<Renderer>().bounds.center))));
            cycle = Vector2.Distance(p, lastTap) < 25 * UiScale && Time.unscaledTime - lastTapTime < 1.5f ? cycle + 1 : 0;
            selected = candidates.Count > 0 ? candidates[cycle % candidates.Count] : null;
            following = false; lastTap = p; lastTapTime = Time.unscaledTime;
        }

        private void OnGUI()
        {
            if (view == null) return;
            GUI.matrix = Matrix4x4.Scale(Vector3.one * UiScale);
            GUILayout.BeginArea(new Rect(12, 12, 410, 450), GUI.skin.box);
            GUILayout.Label("KINGDOM WATCH / TOWN PROTOTYPE");
            GUILayout.Label("80 villagers / 16 houses / seed 731");
            GUILayout.Label("Drag: pan | Wheel / pinch: zoom | Tap: select");
            GUILayout.Label("Repeat a crowd tap to cycle nearby targets.");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Town")) { previousFocus = focus; previousZoom = view.orthographicSize; focus = Vector2.zero; view.orthographicSize = 19; following = false; }
            if (GUILayout.Button("Previous zoom")) { var f = focus; var z = view.orthographicSize; focus = previousFocus; view.orthographicSize = previousZoom; previousFocus = f; previousZoom = z; following = false; }
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Inspect elevated bridge"))
            { previousFocus = focus; previousZoom = view.orthographicSize; focus = new Vector2(23, 0); view.orthographicSize = 8; following = false; }
            GUILayout.Label("Camera rotation: " + Mathf.RoundToInt(yaw) + " degrees");
            yaw = GUILayout.HorizontalSlider(yaw, 0, 360);
            GUILayout.Label("Camera tilt: " + Mathf.RoundToInt(tilt) + " degrees");
            tilt = GUILayout.HorizontalSlider(tilt, 25, 80);
            if (GUILayout.Button("Reset camera angle")) { yaw = 0; tilt = 50; }
            animateSun = GUILayout.Toggle(animateSun, "Move sun automatically (45-second cycle)");
            GUILayout.Label("Sun direction: " + Mathf.RoundToInt(sunAngle) + " degrees");
            float newAngle = GUILayout.HorizontalSlider(sunAngle, 0, 360);
            if (!Mathf.Approximately(newAngle, sunAngle)) { sunAngle = newAngle; animateSun = false; }
            GUILayout.Label(selected == null ? "Select a villager or house." : "Selected: " + selected.name);
            if (selected != null && people.Contains(selected) && GUILayout.Button(following ? "Stop following" : "Follow villager"))
            { previousFocus = focus; previousZoom = view.orthographicSize; following = !following; if (following) view.orthographicSize = 5; }
            GUILayout.EndArea();
            if (selected != null)
            {
                Vector3 screen = view.WorldToScreenPoint(selected.GetComponent<Renderer>().bounds.center);
                GUI.color = Color.yellow;
                GUI.Label(new Rect(screen.x / UiScale - 35, (Screen.height - screen.y) / UiScale - 35, 160, 30), "▼ " + selected.name);
                GUI.color = Color.white;
            }
        }

        private void OnDestroy()
        {
            if (town != null) Destroy(town.gameObject);
            if (view != null && view != sceneCamera) Destroy(view.gameObject);
            foreach (Object asset in owned) if (asset != null) Destroy(asset);
        }
    }
}
