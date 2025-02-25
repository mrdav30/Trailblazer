using FixedMathSharp;

using UnityEngine;

namespace Lockstep.Environment
{
    /// <summary>
    /// Scans and saves environment heightmap based on specificed layers
    /// Must have collider and layer set on object for it to be considered for height map
    /// </summary>
    public class HeightMapSaver : DefaultSaver
    {
        public static HeightMapSaver Instance { get; private set; }

        [SerializeField]
        private Vector2d _size = new Vector2d(100, 100);
        public Vector2d Size => _size;

        [SerializeField]
        private Vector2d _heightBounds = new Vector2d(-20, 50);
        public Vector2d HeightBounds => _heightBounds;

        [SerializeField]
        private Vector2d _bottomLeft = new Vector2d(-50, -50);
        public Vector2d BottomLeft => _bottomLeft;

        [SerializeField]
        private Fixed64 _bonusHeight;
        public Fixed64 BonusHeight => _bonusHeight;

        /// <summary>
        /// Interval distance between each consecutive scan
        /// </summary>
        [SerializeField]
        private Fixed64 _interval = Fixed64.Half;
        public Fixed64 Interval => _interval;

        private const int _compressionShift = FixedMath.SHIFT_AMOUNT_I / 2;

        [SerializeField]
        private HeightMap[] _maps = new HeightMap[1];
        public HeightMap[] Maps => _maps;

        [SerializeField]
        private bool _show;

        public short[,] Scan(int scanLayers)
        {
            int widthPeriods = (Size.x / Interval).CeilToInt();

            int lengthPeriods = (Size.y / Interval).CeilToInt();
            short[,] heightMap = new short[widthPeriods, lengthPeriods];

            Vector3 startPos = _bottomLeft.ToVector3((float)HeightBounds.y);
            Vector3 scanPos = startPos;
            float dist = (float)(HeightBounds.y - HeightBounds.x);

            float fRes = (float)Interval;
            for (int x = 0; x < widthPeriods; x++)
            {
                scanPos.z = startPos.z;
                for (int y = 0; y < lengthPeriods; y++)
                {
                    Fixed64 height;
                    if (Physics.Raycast(scanPos, Vector3.down, out RaycastHit hit, dist, scanLayers, QueryTriggerInteraction.UseGlobal))
                        height = (Fixed64)hit.point.y;
                    else
                        height = HeightBounds.x;

                    heightMap[x, y] = Compress(height);
                    scanPos.z += fRes;
                }

                scanPos.x += fRes;
            }

            return heightMap;
        }

        protected override void OnEarlyApply()
        {
            Instance = this;
        }

        public Fixed64 GetHeight(int mapIndex, Vector2d position)
        {
            if (mapIndex >= Maps.Length)
                return HeightBounds.x;

            HeightMap map = Maps[mapIndex];
            Fixed64 normX = (position.x - _bottomLeft.x) / Interval;
            Fixed64 normY = (position.y - _bottomLeft.y) / Interval;
            int gridX = (int)normX;
            int gridY = (int)normY;
            Fixed64 fractionX = normX - (Fixed64)gridX;
            Fixed64 fractionY = normY - (Fixed64)gridY;
            Fixed64 baseHeight = map.GetHeight(gridX, gridY);

            int nextX = Mathf.Clamp(gridX + 1, 0, map.Map.Width);
            int nextY = Mathf.Clamp(gridY + 1, 0, map.Map.Height);

            //bilinear lerp
            Fixed64 h1 = FixedMath.LinearInterpolate(baseHeight, map.GetHeight(nextX, gridY), fractionX);
            Fixed64 h2 = FixedMath.LinearInterpolate(map.GetHeight(gridX, nextY), map.GetHeight(nextX, nextY), fractionX);
            return FixedMath.LinearInterpolate(h1, h2, fractionY) + BonusHeight;
        }

        private void OnDrawGizmos()
        {
            if (!_show)
                return;

            float fRes = (float)Interval;
            Vector3 size = Vector3.one * (fRes * .95f);
            size.y = .1f;
            Color color = Color.grey;
            for (int i = 0; i < Maps.Length; i++)
            {
                color *= 1.1f;
                HeightMap map = Maps[i];
                Vector3 startPos = BottomLeft.ToVector3(0);
                Vector3 drawPos = startPos;
                for (int x = 0; x < map.Map.Width; x++)
                {
                    drawPos.z = startPos.z;
                    for (int y = 0; y < map.Map.Height; y++)
                    {
                        drawPos.y = (float)map.GetHeight(x, y);
                        Gizmos.DrawCube(drawPos, size);
                        drawPos.z += fRes;
                    }

                    drawPos.x += fRes;
                }
            }
        }

        /*
         [SerializeField]
         Terrain[] _visualizeTerrains;
         
         void SetTerrain(Terrain terrain, float[,] heights)
         {
         terrain.terrainData.SetHeights(0, 0, heights);
         terrain.transform.position = _bottomLeft.ToVector3(0);
         }
         */
        public static short Compress(Fixed64 value)
        {
            long compressed = value.m_rawValue >> _compressionShift;
            if (compressed > short.MaxValue)
                compressed = short.MaxValue;
            else if (compressed < short.MinValue)
                compressed = short.MinValue;

            return (short)compressed;
        }

        public static Fixed64 Uncompress(short compressed)
        {
            return (Fixed64)(compressed << _compressionShift);
        }

        protected override void OnSave()
        {
            var saver = this;
            for (int i = 0; i < saver.Maps.Length; i++)
            {
                HeightMap heightMap = saver.Maps[i];

                short[,] scan = saver.Scan(saver.Maps[i].ScanLayers.value);
                heightMap.Map.AddRange(scan);
            }
            Debug.Log("Height Map Saved");
        }
    }
}