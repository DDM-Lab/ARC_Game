using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// implementation of <see cref="IHighlightManager"/> that creates and dynamically adjusts meshes
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_mesh_highlight_manager.html")]
    public class MeshHighlightManager : MonoBehaviour, IHighlightManager
    {
        [Tooltip("prefab that is instantiated for one highlighting, its mesh is dynamically created and adjusted according to the points")]
        public MeshRenderer Prefab;
        [Tooltip("color set in the mesh renderer for valid points")]
        public Color ValidColor = Color.green;
        [Tooltip("color set in the mesh renderer for invalid points")]
        public Color InvalidColor = Color.red;
        [Tooltip("color set in the mesh renderer for info points")]
        public Color InfoColor = Color.blue;
        [Tooltip(@"show outline around highlights, slower performance due to mesh subdivision and point checking
requires appropriate texture with outer curve, inner curve, line and full fill
the texture has a 2/16th padding to avoid bleed
use outline sprite in Tests project or Highlight sprite in THREE as template")]
        public bool Outline;

        private IMap _map;
        private IGridPositions _gridPositions;
        private IGridHeights _gridHeights;

        private List<MeshRenderer> _used = new List<MeshRenderer>();
        private Queue<MeshRenderer> _pool = new Queue<MeshRenderer>();

        protected virtual void Awake()
        {
            Dependencies.Register<IHighlightManager>(this);
        }

        private void Start()
        {
            _map = Dependencies.Get<IMap>();
            _gridPositions = Dependencies.Get<IGridPositions>();
            _gridHeights = Dependencies.GetOptional<IGridHeights>();
        }

        public void Highlight(IEnumerable<Vector2Int> points, bool valid) => Highlight(points, valid ? ValidColor : InvalidColor);
        public void Highlight(IEnumerable<Vector2Int> points, HighlightType type) => Highlight(points, getColor(type));
        public void Highlight(IEnumerable<Vector2Int> points, Color color)
        {
            MeshRenderer renderer;
            MeshFilter filter;

            if (_pool.Count == 0)
            {
                renderer = Instantiate(Prefab, transform);
                filter = renderer.GetComponent<MeshFilter>();

                filter.mesh = new Mesh();
                filter.mesh.MarkDynamic();
            }
            else
            {
                renderer = _pool.Dequeue();
                filter = renderer.GetComponent<MeshFilter>();
            }

            renderer.gameObject.SetActive(true);
            renderer.material.color = color;

            if (Outline)
                setOutlineMesh(filter.mesh, points);
            else
                setMesh(filter.mesh, points);

            _used.Add(renderer);
        }

        public void Highlight(Vector2Int point, bool isValid) => Highlight(point, isValid ? ValidColor : InvalidColor);
        public void Highlight(Vector2Int point, HighlightType type) => Highlight(point, getColor(type));
        public void Highlight(Vector2Int point, Color color)
        {
            Highlight(new Vector2Int[] { point }, color);
        }

        public void Clear()
        {
            _used.ForEach(u =>
            {
                u.gameObject.SetActive(false);
                _pool.Enqueue(u);
            });
            _used.Clear();
        }

        private Color getColor(HighlightType type)
        {
            switch (type)
            {
                case HighlightType.Valid:
                    return ValidColor;
                case HighlightType.Invalid:
                    return InvalidColor;
                case HighlightType.Info:
                    return InfoColor;
                default:
                    return Color.red;
            }
        }

        private void setMesh(Mesh mesh, IEnumerable<Vector2Int> points)
        {
            var pts = points.ToArray();

            var verts = new List<Vector3>();
            var tris = new List<int>();

            var offset = _map.CellOffset;

            var isXY = _map.IsXY;

            Vector3 p, a, b, c, d;

            for (int i = 0; i < pts.Length; i++)
            {
                p = _gridPositions.GetWorldPosition(pts[i]);

                if (isXY)
                {
                    a = p;
                    b = p + new Vector3(offset.x, 0f, 0f);
                    c = p + new Vector3(0f, offset.y, 0f);
                    d = p + new Vector3(offset.x, offset.y, 0f);

                    if (_gridHeights != null)
                    {
                        a.z = _gridHeights.GetHeight(a);
                        b.z = _gridHeights.GetHeight(b);
                        c.z = _gridHeights.GetHeight(c);
                        d.z = _gridHeights.GetHeight(d);
                    }
                }
                else
                {
                    a = p;
                    b = p + new Vector3(offset.x, 0f, 0f);
                    c = p + new Vector3(0f, 0f, offset.z);
                    d = p + new Vector3(offset.x, 0f, offset.z);

                    if (_gridHeights != null)
                    {
                        a.y = _gridHeights.GetHeight(a);
                        b.y = _gridHeights.GetHeight(b);
                        c.y = _gridHeights.GetHeight(c);
                        d.y = _gridHeights.GetHeight(d);
                    }
                }

                verts.Add(a);
                verts.Add(b);
                verts.Add(c);
                verts.Add(d);

                tris.Add(i * 4 + 0);
                tris.Add(i * 4 + 2);
                tris.Add(i * 4 + 1);
                tris.Add(i * 4 + 2);
                tris.Add(i * 4 + 3);
                tris.Add(i * 4 + 1);
            }

            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.MarkModified();
        }

        private void setOutlineMesh(Mesh mesh, IEnumerable<Vector2Int> points)
        {
            var pts = new HashSet<Vector2Int>(points);

            var verts = new List<Vector3>();
            var tris = new List<int>();
            var uvs = new List<Vector2>();

            var offset = _map.CellOffset;

            var isXY = _map.IsXY;

            Vector3 p, a, b, c, d;

            var i = 0;

            foreach (var pt in pts)
            {
                p = _gridPositions.GetWorldPosition(pt);

                for (int x = 0; x < 2; x++)
                {
                    for (int y = 0; y < 2; y++)
                    {
                        a = p + new Vector3(offset.x / 2f * x, 0f, offset.z / 2f * y);
                        b = a + new Vector3(offset.x / 2f, 0f, 0f);
                        c = a + new Vector3(0f, 0f, offset.z / 2f);
                        d = a + new Vector3(offset.x / 2f, 0f, offset.z / 2f);

                        if (_gridHeights != null)
                        {
                            a.y = _gridHeights.GetHeight(a);
                            b.y = _gridHeights.GetHeight(b);
                            c.y = _gridHeights.GetHeight(c);
                            d.y = _gridHeights.GetHeight(d);
                        }

                        verts.Add(a);
                        verts.Add(b);
                        verts.Add(c);
                        verts.Add(d);
                    }
                }

                for (int j = 0; j < 4; j++)
                {
                    tris.Add(i + 0);
                    tris.Add(i + 2);
                    tris.Add(i + 1);
                    tris.Add(i + 2);
                    tris.Add(i + 3);
                    tris.Add(i + 1);
                    i += 4;
                }

                // MASK
                //
                //  1   TOP
                //  2   RIGHT
                //  4   BOTTOM
                //  8   LEFT

                int mask = pts.Contains(pt + new Vector2Int(0, 1)) ? 1 : 0;
                mask += pts.Contains(pt + new Vector2Int(1, 0)) ? 2 : 0;
                mask += pts.Contains(pt + new Vector2Int(0, -1)) ? 4 : 0;
                mask += pts.Contains(pt + new Vector2Int(-1, 0)) ? 8 : 0;

                //  QUAD ORDER
                //
                //      1 | 3
                //      -----
                //      0 | 2

                switch (mask)
                {
                    //NONE
                    case 0:
                        {
                            addOuterCorner(uvs, 3);
                            addOuterCorner(uvs, 0);
                            addOuterCorner(uvs, 2);
                            addOuterCorner(uvs, 1);
                            break;
                        }
                    ////SINGLE
                    case 1://TOP
                        {
                            addOuterCorner(uvs, 3);
                            addLine(uvs, 0);
                            addOuterCorner(uvs, 2);
                            addLine(uvs, 2);
                            break;
                        }
                    case 2://RIGHT
                        {
                            addOuterCorner(uvs, 3);
                            addOuterCorner(uvs, 0);
                            addLine(uvs, 3);
                            addLine(uvs, 1);
                            break;
                        }
                    case 4://BOTTOM
                        {
                            addLine(uvs, 0);
                            addOuterCorner(uvs, 0);
                            addLine(uvs, 2);
                            addOuterCorner(uvs, 1);
                            break;
                        }
                    case 8://LEFT
                        {
                            addLine(uvs, 3);
                            addLine(uvs, 1);
                            addOuterCorner(uvs, 2);
                            addOuterCorner(uvs, 1);
                            break;
                        }
                    //DOUBLE I
                    case 5:
                        {
                            addLine(uvs, 0);
                            addLine(uvs, 0);
                            addLine(uvs, 2);
                            addLine(uvs, 2);
                            break;
                        }
                    case 10:
                        {
                            addLine(uvs, 3);
                            addLine(uvs, 1);
                            addLine(uvs, 3);
                            addLine(uvs, 1);
                            break;
                        }
                    ////DOUBLE L
                    case 3://TOP RIGHT
                        {
                            addOuterCorner(uvs, 3);
                            addLine(uvs, 0);
                            addLine(uvs, 3);
                            if (pts.Contains(pt + new Vector2Int(1, 1)))
                                addFull(uvs);
                            else
                                addInnerCorner(uvs, 3);
                            break;
                        }
                    case 6://BOTTOM RIGHT
                        {
                            addLine(uvs, 0);
                            addOuterCorner(uvs, 0);
                            if (pts.Contains(pt + new Vector2Int(1, -1)))
                                addFull(uvs);
                            else
                                addInnerCorner(uvs, 0);
                            addLine(uvs, 1);
                            break;
                        }
                    case 9://TOP LEFT
                        {
                            addLine(uvs, 3);
                            if (pts.Contains(pt + new Vector2Int(-1, 1)))
                                addFull(uvs);
                            else
                                addInnerCorner(uvs, 2);
                            addOuterCorner(uvs, 2);
                            addLine(uvs, 2);
                            break;
                        }
                    case 12://BOTTOM LEFT
                        {
                            if (pts.Contains(pt + new Vector2Int(-1, -1)))
                                addFull(uvs);
                            else
                                addInnerCorner(uvs, 1);
                            addLine(uvs, 1);
                            addLine(uvs, 2);
                            addOuterCorner(uvs, 1);
                            break;
                        }
                    ////TRIPLE
                    case 7://LEFT
                        {
                            addLine(uvs, 0);
                            addLine(uvs, 0);
                            if (pts.Contains(pt + new Vector2Int(1, -1)))
                                addFull(uvs);
                            else
                                addInnerCorner(uvs, 0);
                            if (pts.Contains(pt + new Vector2Int(1, 1)))
                                addFull(uvs);
                            else
                                addInnerCorner(uvs, 3);
                            break;
                        }
                    case 11://BOTTOM
                        {
                            addLine(uvs, 3);
                            if (pts.Contains(pt + new Vector2Int(-1, 1)))
                                addFull(uvs);
                            else
                                addInnerCorner(uvs, 2);
                            addLine(uvs, 3);
                            if (pts.Contains(pt + new Vector2Int(1, 1)))
                                addFull(uvs);
                            else
                                addInnerCorner(uvs, 3);
                            break;
                        }
                    case 13://RIGHT
                        {
                            if (pts.Contains(pt + new Vector2Int(-1, -1)))
                                addFull(uvs);
                            else
                                addInnerCorner(uvs, 1);
                            if (pts.Contains(pt + new Vector2Int(-1, 1)))
                                addFull(uvs);
                            else
                                addInnerCorner(uvs, 2);
                            addLine(uvs, 2);
                            addLine(uvs, 2);
                            break;
                        }
                    case 14://TOP
                        {
                            if (pts.Contains(pt + new Vector2Int(-1, -1)))
                                addFull(uvs);
                            else
                                addInnerCorner(uvs, 1);
                            addLine(uvs, 1);
                            if (pts.Contains(pt + new Vector2Int(1, -1)))
                                addFull(uvs);
                            else
                                addInnerCorner(uvs, 0);
                            addLine(uvs, 1);
                            break;
                        }
                    ////FULL
                    case 15:
                        {
                            if (pts.Contains(pt + new Vector2Int(-1, -1)))
                                addFull(uvs);
                            else
                                addInnerCorner(uvs, 1);

                            if (pts.Contains(pt + new Vector2Int(-1, 1)))
                                addFull(uvs);
                            else
                                addInnerCorner(uvs, 2);

                            if (pts.Contains(pt + new Vector2Int(1, -1)))
                                addFull(uvs);
                            else
                                addInnerCorner(uvs, 0);

                            if (pts.Contains(pt + new Vector2Int(1, 1)))
                                addFull(uvs);
                            else
                                addInnerCorner(uvs, 3);
                            break;
                        }
                    default:
                        {
                            for (int j = 0; j < 4; j++)
                            {
                                uvs.Add(new Vector2(0.8f, 0.8f));
                                uvs.Add(new Vector2(0.9f, 0.8f));
                                uvs.Add(new Vector2(0.8f, 0.9f));
                                uvs.Add(new Vector2(0.9f, 0.9f));
                            }
                        }
                        break;

                }
            }

            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.SetUVs(0, uvs);
            mesh.MarkModified();
        }

        private static readonly float TXA = 0.125f;
        private static readonly float TXB = 0.375f;
        private static readonly float TXC = 0.625f;
        private static readonly float TXD = 0.875f;

        private static Vector2[] OUTER_CORNER = new Vector2[]
        {
            new Vector2(TXA, TXC),
            new Vector2(TXB, TXC),
            new Vector2(TXA, TXD),
            new Vector2(TXB, TXD)
        };
        private static Vector2[] INNER_CORNER = new Vector2[]
        {
            new Vector2(TXC, TXC),
            new Vector2(TXD, TXC),
            new Vector2(TXC, TXD),
            new Vector2(TXD, TXD)
        };
        private static Vector2[] LINE = new Vector2[]
        {
            new Vector2(TXA, TXA),
            new Vector2(TXB, TXA),
            new Vector2(TXA, TXB),
            new Vector2(TXB, TXB)
        };
        private static Vector2[] FULL = new Vector2[]
        {
            new Vector2(TXC, TXA),
            new Vector2(TXD, TXA),
            new Vector2(TXC, TXB),
            new Vector2(TXD, TXB)
        };

        private void addUVs(List<Vector2> uvs, Vector2[] shape, int rotation)
        {
            switch (rotation)
            {
                case 0:
                    uvs.Add(shape[0]);
                    uvs.Add(shape[1]);
                    uvs.Add(shape[2]);
                    uvs.Add(shape[3]);
                    break;
                case 1:
                    uvs.Add(shape[1]);
                    uvs.Add(shape[3]);
                    uvs.Add(shape[0]);
                    uvs.Add(shape[2]);
                    break;
                case 2:
                    uvs.Add(shape[3]);
                    uvs.Add(shape[2]);
                    uvs.Add(shape[1]);
                    uvs.Add(shape[0]);
                    break;
                case 3:
                    uvs.Add(shape[2]);
                    uvs.Add(shape[0]);
                    uvs.Add(shape[3]);
                    uvs.Add(shape[1]);
                    break;
            }
        }

        private void addOuterCorner(List<Vector2> uvs, int rotation) => addUVs(uvs, OUTER_CORNER, rotation);
        private void addInnerCorner(List<Vector2> uvs, int rotation) => addUVs(uvs, INNER_CORNER, rotation);
        private void addLine(List<Vector2> uvs, int rotation) => addUVs(uvs, LINE, rotation);
        private void addFull(List<Vector2> uvs) => addUVs(uvs, FULL, 0);
    }
}