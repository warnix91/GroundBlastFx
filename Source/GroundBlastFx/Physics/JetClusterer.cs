namespace GroundBlastFx.Model
{
    /// <summary>
    /// Fusion des taches d'impact proches en foyers par union-find.
    /// Deux jets fusionnent si leurs impacts sont à moins de mergeFactor × max(r_i, r_j) et s'ils ont la même clé
    /// (la clé sépare par exemple les foyers de démonstration des foyers réels). La fusion est transitive :
    /// les 33 moteurs d'un Super Heavy forment un seul foyer.
    /// Aucune allocation après le premier appel à une taille donnée.
    /// </summary>
    public sealed class JetClusterer
    {
        private int[] _parent = new int[64];
        private int[] _groupOfRoot = new int[64];

        /// <returns>Nombre de groupes ; <paramref name="groupOut"/>[i] = indice de groupe du jet i (0..n-1, dans l'ordre d'apparition).</returns>
        public int Cluster(int count, float[] x, float[] y, float[] z, float[] radius, int[] key, float mergeFactor, int[] groupOut)
        {
            if (count <= 0) return 0;
            EnsureCapacity(count);
            for (int i = 0; i < count; i++) _parent[i] = i;

            for (int i = 0; i < count; i++)
            {
                for (int j = i + 1; j < count; j++)
                {
                    if (key[i] != key[j]) continue;
                    float dx = x[i] - x[j], dy = y[i] - y[j], dz = z[i] - z[j];
                    float lim = mergeFactor * (radius[i] > radius[j] ? radius[i] : radius[j]);
                    if (dx * dx + dy * dy + dz * dz < lim * lim) Union(i, j);
                }
            }

            for (int i = 0; i < count; i++) _groupOfRoot[i] = -1;
            int groups = 0;
            for (int i = 0; i < count; i++)
            {
                int root = Find(i);
                if (_groupOfRoot[root] < 0) _groupOfRoot[root] = groups++;
                groupOut[i] = _groupOfRoot[root];
            }
            return groups;
        }

        private void EnsureCapacity(int count)
        {
            if (_parent.Length >= count) return;
            int n = _parent.Length;
            while (n < count) n *= 2;
            _parent = new int[n];
            _groupOfRoot = new int[n];
        }

        private int Find(int i)
        {
            while (_parent[i] != i)
            {
                _parent[i] = _parent[_parent[i]];
                i = _parent[i];
            }
            return i;
        }

        private void Union(int a, int b)
        {
            int ra = Find(a), rb = Find(b);
            if (ra == rb) return;
            // Racine = plus petit indice : ordre des groupes stable d'un sondage à l'autre.
            if (ra < rb) _parent[rb] = ra; else _parent[ra] = rb;
        }
    }
}
