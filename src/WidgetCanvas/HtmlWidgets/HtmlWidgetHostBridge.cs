#nullable enable

namespace WidgetCanvas.HtmlWidgets
{
    internal static class HtmlWidgetHostBridge
    {
        /// <summary>
        /// 仅向组件的顶层文档公开宿主能力。子框架可以正常渲染内容，
        /// 但不会自动获得文件、进程和剪贴板等桌面能力。
        /// </summary>
        public const string Script = """
            (() => {
              if (window.top !== window) return;
              if (window.widgetHost?.state?.read) return;
              const pending = new Map();
              let sequence = 0;
              chrome.webview.addEventListener('message', event => {
                const message = event.data;
                if (!message || message.type !== 'widgetHostReply') return;
                const request = pending.get(message.id);
                if (!request) return;
                pending.delete(message.id);
                if (message.ok) request.resolve(message.result);
                else request.reject(new Error(message.error || '宿主调用失败'));
              });
              const invoke = (method, args = {}) => new Promise((resolve, reject) => {
                const id = `${Date.now()}-${++sequence}`;
                pending.set(id, { resolve, reject });
                try {
                  chrome.webview.postMessage({ id, method, args });
                } catch (error) {
                  pending.delete(id);
                  reject(error);
                }
              });
              window.widgetHost = {
                state: {
                  read: (key, defaultValue = null) => invoke('state.read', { key, defaultValue }),
                  write: (key, value) => invoke('state.write', { key, value }),
                  remove: key => invoke('state.remove', { key }),
                  clear: () => invoke('state.clear'),
                  flush: () => invoke('state.flush')
                },
                clipboard: {
                  read: () => invoke('clipboard.read'),
                  write: text => invoke('clipboard.write', { text })
                },
                url: {
                  open: (args = {}) => invoke('url.open', args)
                },
                path: {
                  open: (args = {}) => invoke('path.open', args)
                },
                window: {
                  hide: () => invoke('window.hide')
                },
                http: {
                  get: (args = {}) => invoke('http.get', args),
                  post: (args = {}) => invoke('http.post', args),
                  request: (args = {}) => invoke('http.request', args)
                },
                fs: {
                  getKnownFolders: () => invoke('fs.getKnownFolders'),
                  exists: (args = {}) => invoke('fs.exists', args),
                  getInfo: (args = {}) => invoke('fs.getInfo', args),
                  readText: (args = {}) => invoke('fs.readText', args),
                  readBase64: (args = {}) => invoke('fs.readBase64', args),
                  list: (args = {}) => invoke('fs.list', args),
                  selectFile: (args = {}) => invoke('fs.selectFile', args),
                  selectFolder: (args = {}) => invoke('fs.selectFolder', args)
                },
                process: {
                  start: (args = {}) => invoke('process.start', args),
                  run: (args = {}) => invoke('process.run', args)
                }
              };
              const reportRuntimeError = (message, stack = '') =>
                chrome.webview.postMessage({
                  type: 'runtimeError',
                  message: String(message || '未知脚本错误'),
                  stack: String(stack || '')
                });
              window.addEventListener('error', event =>
                reportRuntimeError(event.message, event.error?.stack || ''), true);
              window.addEventListener('unhandledrejection', event => {
                const reason = event.reason;
                reportRuntimeError(reason?.message || reason || '未处理的异步错误', reason?.stack || '');
              }, true);
              document.addEventListener('compositionstart', () =>
                chrome.webview.postMessage({ type: 'compositionState', active: true }), true);
              document.addEventListener('compositionend', () =>
                chrome.webview.postMessage({ type: 'compositionState', active: false }), true);
            })();
            """;
    }
}
