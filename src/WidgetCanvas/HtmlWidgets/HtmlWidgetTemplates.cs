#nullable enable

namespace WidgetCanvas.HtmlWidgets
{
    internal static class HtmlWidgetTemplates
    {
        public const string QuickNoteHtml = """
            <!doctype html>
            <html lang="zh-CN">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width,initial-scale=1">
              <title>便签</title>
              <style>
                :root { color-scheme:light;--ink:#454238;--muted:#817c6d;--line:rgba(75,72,55,.12);--paper:rgba(244,242,226,.98);--hover:rgba(89,86,65,.08);--accent:#716743; }
                * { box-sizing:border-box; }
                html,body { width:100%;height:100%;margin:0;overflow:hidden;background:transparent; }
                body { font-family:"Microsoft YaHei",sans-serif;color:var(--ink);user-select:none; }
                button,textarea { font:inherit; }
                .note { width:100%;height:100%;display:flex;flex-direction:column;overflow:hidden;border:1px solid rgba(73,70,50,.15);border-radius:12px;background:var(--paper);box-shadow:0 8px 28px rgba(42,40,29,.07); }
                header { min-height:43px;display:flex;align-items:center;gap:8px;padding:7px 9px 7px 13px;border-bottom:1px solid var(--line); }
                h1 { margin:0 auto 0 0;font-size:14px;line-height:20px;font-weight:700; }
                button { height:28px;padding:0 9px;border:1px solid transparent;border-radius:7px;background:transparent;color:#656153;font-size:11px;cursor:pointer;outline:none; }
                button:hover { background:var(--hover);border-color:var(--line); }
                button:active { transform:translateY(1px); }
                button:focus-visible { border-color:rgba(113,103,67,.48);box-shadow:0 0 0 2px rgba(113,103,67,.1); }
                button.danger { color:#9c5b52; }
                textarea { flex:1;min-height:0;width:100%;resize:none;border:0;outline:0;padding:13px 15px 9px;background:repeating-linear-gradient(to bottom,transparent 0,transparent 27px,rgba(75,72,55,.075) 28px);color:var(--ink);font-size:14px;line-height:28px;user-select:text;scrollbar-width:thin;scrollbar-color:rgba(75,72,55,.18) transparent; }
                textarea::placeholder { color:#9d9889; }
                footer { min-height:28px;display:flex;align-items:center;padding:3px 13px 6px;color:var(--muted);font-size:10px; }
                #status { margin-right:auto; }
                #status.error { color:#a55750; }
                @media(max-width:330px) { header { padding-left:10px;gap:3px; } button { padding:0 6px; } textarea { padding-left:12px;padding-right:12px; } }
                @media(max-width:190px) { header { justify-content:flex-end;padding-left:6px;padding-right:6px;gap:1px; } h1 { display:none; } button { height:26px;padding:0 5px;font-size:10px; } }
                @media(max-height:190px) { header { min-height:36px;padding-top:4px;padding-bottom:4px; } footer { display:none; } textarea { padding-top:8px; } }
              </style>
            </head>
            <body>
              <main class="note">
                <header>
                  <h1>便签</h1>
                  <button id="insertBtn" type="button" title="在光标处插入剪贴板文本">插入</button>
                  <button id="copyBtn" type="button" title="复制全部便签内容">复制</button>
                  <button id="clearBtn" class="danger" type="button" title="清空便签内容">清空</button>
                </header>
                <textarea id="text" spellcheck="false" maxlength="100000" placeholder="随手记点什么……" aria-label="便签内容"></textarea>
                <footer><span id="status">正在读取…</span><span id="count">0 字</span></footer>
              </main>
              <script>
                (() => {
                  "use strict";
                  const host = window.widgetHost;
                  const text = document.getElementById("text");
                  const status = document.getElementById("status");
                  const count = document.getElementById("count");
                  const insertBtn = document.getElementById("insertBtn");
                  const copyBtn = document.getElementById("copyBtn");
                  const clearBtn = document.getElementById("clearBtn");
                  let saveVersion = 0;
                  let clearTimer = 0;
                  let clearArmed = false;

                  function setStatus(message, error = false) {
                    status.textContent = message;
                    status.classList.toggle("error", error);
                    status.title = error ? message : "";
                  }
                  function updateCount() { count.textContent = text.value.length + " 字"; }
                  async function save(value = text.value) {
                    const version = ++saveVersion;
                    setStatus("正在保存…");
                    try {
                      await host.state.write("text", value);
                      if (version === saveVersion) setStatus("已自动保存");
                    } catch (error) {
                      if (version === saveVersion) {
                        setStatus("保存失败：" + String(error?.message || error), true);
                      }
                    }
                  }
                  function saveCurrent() {
                    updateCount();
                    void save(text.value);
                  }
                  text.addEventListener("input", saveCurrent);
                  insertBtn.addEventListener("click", async () => {
                    try {
                      const value = await host.clipboard.read();
                      if (!value) { setStatus("剪贴板没有文本"); return; }
                      const start = text.selectionStart;
                      const end = text.selectionEnd;
                      text.setRangeText(value, start, end, "end");
                      text.focus();
                      saveCurrent();
                    } catch (error) {
                      setStatus("读取失败：" + String(error?.message || error), true);
                    }
                  });
                  copyBtn.addEventListener("click", async () => {
                    if (!text.value) { setStatus("没有可复制的内容"); return; }
                    try {
                      await host.clipboard.write(text.value);
                      setStatus("已复制");
                    } catch (error) {
                      setStatus("复制失败：" + String(error?.message || error), true);
                    }
                  });
                  clearBtn.addEventListener("click", () => {
                    if (!text.value) return;
                    if (!clearArmed) {
                      clearArmed = true;
                      clearBtn.textContent = "再点一次";
                      setStatus("再次点击确认清空");
                      clearTimer = window.setTimeout(() => {
                        clearArmed = false;
                        clearBtn.textContent = "清空";
                      }, 2200);
                      return;
                    }
                    window.clearTimeout(clearTimer);
                    clearArmed = false;
                    clearBtn.textContent = "清空";
                    text.value = "";
                    updateCount();
                    void save("");
                    text.focus();
                  });
                  (async () => {
                    try {
                      const saved = await host.state.read("text", "");
                      text.value = typeof saved === "string" ? saved : "";
                      updateCount();
                      setStatus("已自动保存");
                    } catch (error) {
                      setStatus("读取失败：" + String(error?.message || error), true);
                    }
                  })();
                })();
              </script>
            </body>
            </html>
            """;
    }
}
