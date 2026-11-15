using System.Text.Json;
using System.Text.RegularExpressions;

namespace SwirStreamAI;

public record IntentPrediction(string Intent, double Confidence, double Margin, bool Accepted);
public sealed class IntentNet
{
    public string Format { get; set; } = "SwirIntentNet-1";
    public int Inputs { get; set; } = 1024;
    public int Hidden { get; set; } = 64;
    public string[] Labels { get; set; } = [];
    public double[] W1 { get; set; } = [];
    public double[] B1 { get; set; } = [];
    public double[] W2 { get; set; } = [];
    public double[] B2 { get; set; } = [];
    public string[] Vocabulary { get; set; } = [];
    public int TrainingExamples { get; set; }
    public static string[] Words(string text) => Regex.Matches(TextTools.Normalize(text), "[a-z0-9]+").Select(m => m.Value).ToArray();
    static uint Hash(string text) { uint v = 2166136261; foreach (char c in text) v = unchecked((v ^ c) * 16777619); return v; }
    public double[] Features(string text)
    {
        var x = new double[Inputs]; var words = Words(text);
        foreach (var w in words)
        {
            x[Hash("w:" + w) % Inputs] += 2;
            string pad = "^" + w + "$";
            for (int i = 0; i + 3 <= pad.Length; i++) x[Hash("c:" + pad.Substring(i, 3)) % Inputs] += .45;
        }
        for (int i = 0; i + 1 < words.Length; i++) x[Hash("b:" + words[i] + " " + words[i + 1]) % Inputs] += 1;
        double norm = Math.Sqrt(x.Sum(v => v * v));
        if (norm > 0) for (int i = 0; i < x.Length; i++) x[i] /= norm;
        return x;
    }
    (double[] h, double[] p) Forward(double[] x)
    {
        var h = new double[Hidden]; var p = new double[Labels.Length];
        for (int j = 0; j < Hidden; j++)
        {
            double z = B1[j]; for (int i = 0; i < Inputs; i++) if (x[i] != 0) z += W1[j * Inputs + i] * x[i];
            h[j] = Math.Tanh(z);
        }
        for (int k = 0; k < p.Length; k++) { p[k] = B2[k]; for (int j = 0; j < Hidden; j++) p[k] += W2[k * Hidden + j] * h[j]; }
        double max = p.Max(); for (int k = 0; k < p.Length; k++) p[k] = Math.Exp(p[k] - max);
        double sum = p.Sum(); for (int k = 0; k < p.Length; k++) p[k] /= sum;
        return (h, p);
    }
    public IntentPrediction Predict(string text)
    {
        var (_, p) = Forward(Features(text)); var order = Enumerable.Range(0, p.Length).OrderByDescending(i => p[i]).ToArray();
        double score = p[order[0]], margin = score - p[order[1]];
        bool known = Words(text).Any(w => Vocabulary.Contains(w));
        return new(Labels[order[0]], score, margin, known && score >= .62 && margin >= .25);
    }
    public static IntentNet Train(Dictionary<string, string[]> data)
    {
        var model = new IntentNet { Labels = data.Keys.ToArray(), TrainingExamples = data.Sum(d => d.Value.Length) };
        var random = new Random(4019);
        model.W1 = Enumerable.Range(0, model.Inputs * model.Hidden).Select(_ => (random.NextDouble() - .5) * .12).ToArray();
        model.B1 = new double[model.Hidden];
        model.W2 = Enumerable.Range(0, model.Hidden * model.Labels.Length).Select(_ => (random.NextDouble() - .5) * .2).ToArray();
        model.B2 = new double[model.Labels.Length];
        model.Vocabulary = data.Values.SelectMany(v => v).SelectMany(Words).Distinct().Order().ToArray();
        var examples = data.SelectMany((d, label) => d.Value.Select(s => (x: model.Features(s), label))).ToArray();
        for (int epoch = 0; epoch < 100; epoch++)
        {
            random.Shuffle(examples);
            double rate = .12 * (1 - .65 * epoch / 100.0);
            foreach (var (x, label) in examples)
            {
                var (h, p) = model.Forward(x); p[label] -= 1;
                var dh = new double[model.Hidden];
                for (int j = 0; j < model.Hidden; j++)
                {
                    double s = 0; for (int k = 0; k < p.Length; k++) s += p[k] * model.W2[k * model.Hidden + j];
                    dh[j] = s * (1 - h[j] * h[j]);
                }
                for (int k = 0; k < p.Length; k++)
                {
                    model.B2[k] -= rate * p[k];
                    for (int j = 0; j < model.Hidden; j++) model.W2[k * model.Hidden + j] -= rate * (p[k] * h[j] + .0001 * model.W2[k * model.Hidden + j]);
                }
                for (int j = 0; j < model.Hidden; j++)
                {
                    model.B1[j] -= rate * dh[j];
                    for (int i = 0; i < model.Inputs; i++) if (x[i] != 0) model.W1[j * model.Inputs + i] -= rate * dh[j] * x[i];
                }
            }
        }
        return model;
    }
    public static IntentNet Load(string path, int expectedLabels = 19)
    {
        var m = JsonSerializer.Deserialize<IntentNet>(File.ReadAllText(path)) ?? throw new InvalidDataException("Brak modelu AI.");
        if (m.Format != "SwirIntentNet-1" || m.Inputs != 1024 || m.Hidden != 64 || m.Labels.Length != expectedLabels ||
            m.W1.Length != m.Inputs * m.Hidden || m.W2.Length != m.Hidden * expectedLabels || m.B1.Length != m.Hidden || m.B2.Length != expectedLabels)
            throw new InvalidDataException("Nieprawidłowy model Swir IntentNet.");
        return m;
    }
    public static string Query(string text)
    {
        string q = TextTools.Normalize(text);
        q = Regex.Replace(q, @"\b(znajdz|wyszukaj|szukaj|pokaz|prosze|mi|chce|wlacz|odtworz|posluchac|obejrzec|jakies|jakis|radio|radia|radiowe|stacje|stacji|telewizje|telewizja|telewizyjne|tv|kanaly|kanalow|transmisje|transmisji|streamy|streamow|live|na zywo)\b", " ");
        return Regex.Replace(q, @"\s+", " ").Trim();
    }
}
