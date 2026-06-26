module CrawlSage.Tests.Charset

open System.Text
open Xunit
open CrawlSage

let private encode (charset: string) (text: string) : byte[] =
    // Register the legacy code pages so EUC-KR / Shift_JIS / GBK resolve when building test bytes.
    // (Production Http registers them at module load; doing it here keeps the test order-independent.)
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)
    Encoding.GetEncoding(charset).GetBytes text

[<Fact>]
let ``decode uses the HTTP header charset when present`` () =
    let korean = "안녕하세요"
    Assert.Equal(korean, Http.decode (Some "euc-kr") (encode "euc-kr" korean))

[<Fact>]
let ``decode sniffs the <meta charset> when no header charset`` () =
    let korean = "한글 페이지입니다"
    let html = sprintf """<html><head><meta charset="euc-kr"></head><body>%s</body></html>""" korean
    // No HTTP header charset — the meta declaration must be honoured, or this is mojibake.
    Assert.Contains(korean, Http.decode None (encode "euc-kr" html))

[<Fact>]
let ``decode handles the http-equiv meta form`` () =
    let korean = "정보"

    let html =
        sprintf
            """<html><head><meta http-equiv="Content-Type" content="text/html; charset=euc-kr"></head><body>%s</body></html>"""
            korean

    Assert.Contains(korean, Http.decode None (encode "euc-kr" html))

[<Fact>]
let ``decode defaults to UTF-8 when nothing declares a charset`` () =
    let text = "plain ünïcødé 가나다"
    Assert.Equal(text, Http.decode None (Encoding.UTF8.GetBytes text))

[<Fact>]
let ``decode lets a BOM override the fallback hint`` () =
    let text = "BOM 우선 test"
    let bytes = Array.append (Encoding.UTF8.GetPreamble()) (Encoding.UTF8.GetBytes text)
    // Even with a deliberately-wrong euc-kr hint, the UTF-8 BOM wins.
    Assert.Equal(text, Http.decode (Some "euc-kr") bytes)
