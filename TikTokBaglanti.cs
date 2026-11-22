
using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

namespace MuhtesemSaray
{
    public enum OlayTuru
    {
        Yorum,
        Hediye,
        Begeni,
        Takip,
        Paylasim,
        Giris
    }

    public class TikTokOlay
    {
        public OlayTuru tur;
        public string id;
        public string ad;
        public string resim;
        public string yorum;
        public string hediyeAdi;
        public int coin;
        public int adet;
    }

    [Serializable]
    public class Paket
    {
        public string @event;
        public Veri data;
    }

    [Serializable]
    public class Veri
    {
        public string uniqueId;
        public string nickname;
        public string profilePictureUrl;
        public string comment;
        public string giftName;
        public int diamondCount;
        public int repeatCount;
        public bool repeatEnd;
        public int giftType;
        public int likeCount;
    }

    public class TikTokBaglanti : MonoBehaviour
    {
        [Header("Baglanti")]
        public string adres = "ws://localhost:21213/";
        public bool baglan = true;

        [Min(1)]
        public int yenidenBaglanSaniye = 3;

        [Min(1024)]
        public int azamiMesajBoyutu = 1024 * 1024;

        [Header("Test")]
        public bool testTuslari = true;

        public readonly ConcurrentQueue<TikTokOlay> kuyruk =
            new ConcurrentQueue<TikTokOlay>();

        public bool Bagli { get; private set; }

        public string SonBaglantiHatasi { get; private set; }

        private CancellationTokenSource iptal;
        private Task donguGorevi;
        private int testSayac;
        private int ornekSayisi;

        private static readonly System.Text.RegularExpressions.Regex urlDeseni =
            new System.Text.RegularExpressions.Regex(
                "\"(https?:[^\"]+?)\"",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        public bool TestAcik
        {
            get { return testTuslari && Application.isEditor; }
        }

        private void OnEnable()
        {
            Baslat();
        }

        private void OnDisable()
        {
            Durdur();
        }

        private void OnDestroy()
        {
            Durdur();
        }

        private void Baslat()
        {
            if (!baglan || iptal != null)
                return;

            iptal = new CancellationTokenSource();
            donguGorevi = Dongu(iptal.Token);
        }

        private void Durdur()
        {
            if (iptal != null)
            {
                try
                {
                    iptal.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }

                iptal = null;
            }

            Bagli = false;
        }

        private async Task Dongu(CancellationToken ct)
        {
            byte[] tampon = new byte[8192];

            while (!ct.IsCancellationRequested)
            {
                using (ClientWebSocket ws = new ClientWebSocket())
                {
                    try
                    {
                        Uri hedef;

                        if (!Uri.TryCreate(
                            adres,
                            UriKind.Absolute,
                            out hedef) ||
                            (hedef.Scheme != "ws" &&
                             hedef.Scheme != "wss"))
                        {
                            SonBaglantiHatasi =
                                "Gecersiz WebSocket adresi: " + adres;

                            Debug.LogError(
                                "[MuhtesemSaray] " +
                                SonBaglantiHatasi);

                            break;
                        }

                        await ws.ConnectAsync(hedef, ct);

                        Bagli = true;
                        SonBaglantiHatasi = null;

                        Debug.Log(
                            "[MuhtesemSaray] TikTok baglantisi kuruldu: " +
                            adres);

                        StringBuilder sb = new StringBuilder();

                        while (
                            ws.State == WebSocketState.Open &&
                            !ct.IsCancellationRequested)
                        {
                            WebSocketReceiveResult sonuc =
                                await ws.ReceiveAsync(
                                    new ArraySegment<byte>(tampon),
                                    ct);

                            if (sonuc.MessageType ==
                                WebSocketMessageType.Close)
                            {
                                try
                                {
                                    if (ws.State ==
                                        WebSocketState.CloseReceived)
                                    {
                                        await ws.CloseOutputAsync(
                                            WebSocketCloseStatus.NormalClosure,
                                            "Kapatiliyor",
                                            CancellationToken.None);
                                    }
                                }
                                catch
                                {
                                }

                                break;
                            }

                            if (sonuc.MessageType !=
                                WebSocketMessageType.Text)
                            {
                                if (sonuc.EndOfMessage)
                                    sb.Length = 0;

                                continue;
                            }

                            if (sb.Length + sonuc.Count >
                                Mathf.Max(1024, azamiMesajBoyutu))
                            {
                                Debug.LogWarning(
                                    "[MuhtesemSaray] WebSocket mesaji " +
                                    "boyut sinirini asti; paket atlandi.");

                                sb.Length = 0;
                                continue;
                            }

                            sb.Append(
                                Encoding.UTF8.GetString(
                                    tampon,
                                    0,
                                    sonuc.Count));

                            if (sonuc.EndOfMessage)
                            {
                                Coz(sb.ToString());
                                sb.Length = 0;
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (WebSocketException e)
                    {
                        SonBaglantiHatasi = e.Message;

                        if (!ct.IsCancellationRequested)
                        {
                            Debug.LogWarning(
                                "[MuhtesemSaray] WebSocket hatasi: " +
                                e.Message);
                        }
                    }
                    catch (Exception e)
                    {
                        SonBaglantiHatasi = e.Message;

                        if (!ct.IsCancellationRequested)
                        {
                            Debug.LogWarning(
                                "[MuhtesemSaray] Baglanti hatasi: " +
                                e.Message);
                        }
                    }
                    finally
                    {
                        Bagli = false;
                    }
                }

                if (!ct.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(
                            Mathf.Max(1, yenidenBaglanSaniye) * 1000,
                            ct);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }
            }

            Bagli = false;
        }

        private static string ResimSec(
            string json,
            string varsayilan)
        {
            string uygun = null;

            foreach (
                System.Text.RegularExpressions.Match m
                in urlDeseni.Matches(json))
            {
                string url = m.Groups[1].Value
                    .Replace("\\/", "/")
                    .Replace("\\u0026", "&")
                    .Replace("&amp;", "&");

                string kucuk = url.ToLowerInvariant();

                if (!kucuk.Contains("tiktokcdn") &&
                    !kucuk.Contains("tiktok"))
                    continue;

                if (kucuk.Contains("webcast"))
                    continue;

                if (kucuk.Contains("avt") ||
                    kucuk.Contains("avatar"))
                {
                    if (kucuk.Contains(".jpeg") ||
                        kucuk.Contains(".jpg") ||
                        kucuk.Contains(".png") ||
                        kucuk.Contains(".webp"))
                    {
                        return url;
                    }

                    if (uygun == null)
                        uygun = url;
                }
            }

            return uygun ?? varsayilan;
        }

        private void OrnekKaydet(string json)
        {
            if (ornekSayisi >= 40)
                return;

            ornekSayisi++;

            try
            {
                string yol = System.IO.Path.Combine(
                    Application.persistentDataPath,
                    "tiktok_ornek_olaylar.txt");

                System.IO.File.AppendAllText(
                    yol,
                    json + Environment.NewLine +
                    Environment.NewLine);
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    "[MuhtesemSaray] Ornek olay kaydedilemedi: " +
                    e.Message);
            }
        }

        private void Coz(string json)
        {
            if (string.IsNullOrEmpty(json))
                return;

            OrnekKaydet(json);

            Paket paket;

            try
            {
                paket = JsonUtility.FromJson<Paket>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    "[MuhtesemSaray] JSON okunamadi: " +
                    e.Message);

                return;
            }

            if (paket == null ||
                paket.data == null ||
                string.IsNullOrEmpty(paket.@event))
                return;

            Veri veri = paket.data;

            string kullaniciId =
                !string.IsNullOrEmpty(veri.uniqueId)
                ? veri.uniqueId
                : veri.nickname;

            if (string.IsNullOrEmpty(kullaniciId))
                return;

            TikTokOlay olay = new TikTokOlay
            {
                id = kullaniciId,

                ad = !string.IsNullOrEmpty(veri.nickname)
                    ? veri.nickname
                    : kullaniciId,

                resim = ResimSec(
                    json,
                    veri.profilePictureUrl)
            };

            switch (paket.@event.ToLowerInvariant())
            {
                case "chat":

                    olay.tur = OlayTuru.Yorum;
                    olay.yorum = veri.comment ?? "";

                    break;

                case "gift":

                    if (veri.giftType == 1 &&
                        !veri.repeatEnd)
                        return;

                    olay.tur = OlayTuru.Hediye;
                    olay.hediyeAdi = veri.giftName ?? "";

                    long toplamCoin =
                        (long)Math.Max(0, veri.diamondCount) *
                        Math.Max(1, veri.repeatCount);

                    olay.coin = (int)Math.Min(
                        int.MaxValue,
                        Math.Max(1L, toplamCoin));

                    break;

                case "like":

                    olay.tur = OlayTuru.Begeni;
                    olay.adet = Math.Max(1, veri.likeCount);

                    break;

                case "follow":

                    olay.tur = OlayTuru.Takip;

                    break;

                case "share":

                    olay.tur = OlayTuru.Paylasim;

                    break;

                case "member":

                    olay.tur = OlayTuru.Giris;

                    break;

                default:

                    return;
            }

            kuyruk.Enqueue(olay);
        }

        private void Update()
        {
            if (baglan &&
                iptal == null &&
                isActiveAndEnabled)
            {
                Baslat();
            }

            if (!TestAcik)
                return;

            if (Bas('H'))
                SahteKatil("1");

            if (Bas('D'))
                SahteKatil("2");

            if (Bas('K'))
                SahteKatil("katil");

            if (Bas('S'))
                SahteKatil("sultan");

            if (Bas('1'))
                SahteHediye("", 1, "Ice Cream Cone");

            if (Bas('2'))
                SahteHediye("hurrem", 99);

            if (Bas('3'))
                SahteHediye("hurrem", 500);

            if (Bas('4'))
                SahteHediye("hurrem", 1000);

            if (Bas('Q'))
                SahteHediye("", 1, "GG");

            if (Bas('W'))
                SahteHediye("devran", 99);

            if (Bas('E'))
                SahteHediye("devran", 500);

            if (Bas('R'))
                SahteHediye("devran", 1000);

            if (Bas('T'))
                SahteHediye("", 100, "Mishka Bear");

            if (Bas('X'))
            {
                kuyruk.Enqueue(new TikTokOlay
                {
                    tur = OlayTuru.Yorum,
                    id = "test_" + testSayac,
                    ad = "test",
                    yorum = "erkek"
                });
            }

            if (Bas('Z'))
            {
                kuyruk.Enqueue(new TikTokOlay
                {
                    tur = OlayTuru.Yorum,
                    id = "test_" + testSayac,
                    ad = "test",
                    yorum = "kadin"
                });
            }

            if (Bas('L'))
            {
                kuyruk.Enqueue(new TikTokOlay
                {
                    tur = OlayTuru.Begeni,
                    id = "test_" + testSayac,
                    ad = "test",
                    adet = 50
                });
            }

            if (Bas('G'))
            {
                testSayac++;

                kuyruk.Enqueue(new TikTokOlay
                {
                    tur = OlayTuru.Takip,
                    id = "test_" + testSayac,
                    ad = "test_takipci" + testSayac
                });
            }
        }

        private void SahteKatil(string komut)
        {
            testSayac++;

            kuyruk.Enqueue(new TikTokOlay
            {
                tur = OlayTuru.Yorum,
                id = "test_" + testSayac,
                ad = "test_izleyici" + testSayac,
                yorum = komut
            });
        }

        private void SahteHediye(
            string taraf,
            int coin,
            string hediye = "Test")
        {
            testSayac++;

            string id = "test_" + testSayac;

            if (!string.IsNullOrEmpty(taraf))
            {
                kuyruk.Enqueue(new TikTokOlay
                {
                    tur = OlayTuru.Yorum,
                    id = id,
                    ad = id,
                    yorum = taraf
                });
            }

            kuyruk.Enqueue(new TikTokOlay
            {
                tur = OlayTuru.Hediye,
                id = id,
                ad = id,
                hediyeAdi = hediye,
                coin = Math.Max(1, coin)
            });
        }

        public static bool Bas(char c)
        {
#if ENABLE_LEGACY_INPUT_MANAGER

            KeyCode tus = (c >= '0' && c <= '9')
                ? (KeyCode)(
                    (int)KeyCode.Alpha0 + (c - '0'))
                : (KeyCode)Enum.Parse(
                    typeof(KeyCode),
                    c.ToString());

            return Input.GetKeyDown(tus);

#elif ENABLE_INPUT_SYSTEM

            Keyboard klavye = Keyboard.current;

            if (klavye == null)
                return false;

            Key tus = c == '0'
                ? Key.Digit0
                : (c >= '1' && c <= '9')
                    ? (Key)(
                        (int)Key.Digit1 + (c - '1'))
                    : (Key)Enum.Parse(
                        typeof(Key),
                        c.ToString());

            return klavye[tus].wasPressedThisFrame;

#else

            return false;

#endif
        }
    }
}
