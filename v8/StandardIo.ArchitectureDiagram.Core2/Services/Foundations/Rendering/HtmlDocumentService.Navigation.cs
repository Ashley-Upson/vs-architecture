using System.Xml.Linq;

namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;

internal sealed partial class HtmlDocumentService
{
    private static XElement Button(string id, string text, string label) =>
        new("button", new XAttribute("id", id), new XAttribute("type", "button"), new XAttribute("aria-label", label), text);

    private const string NavigationScript = """
        (function () {
            var viewport = document.getElementById('viewport');
            var canvas = viewport.querySelector('svg');
            var width = Number(canvas.getAttribute('width'));
            var height = Number(canvas.getAttribute('height'));
            var scale = 1;
            function zoom(value, x, y) {
                var next = Math.max(0.0001, Math.min(4, value));
                var cx = x == null ? viewport.clientWidth / 2 : x;
                var cy = y == null ? viewport.clientHeight / 2 : y;
                var left = (viewport.scrollLeft + cx) / scale;
                var top = (viewport.scrollTop + cy) / scale;
                scale = next;
                canvas.style.width = (width * scale) + 'px';
                canvas.style.height = (height * scale) + 'px';
                viewport.scrollLeft = left * scale - cx;
                viewport.scrollTop = top * scale - cy;
                document.getElementById('zoom-level').textContent = (scale * 100).toFixed(1) + '%';
            }
            document.getElementById('zoom-in').onclick = function () { zoom(scale * 1.25); };
            document.getElementById('zoom-out').onclick = function () { zoom(scale / 1.25); };
            document.getElementById('zoom-reset').onclick = function () { zoom(1); };
            function fit() {
                zoom(viewport.clientWidth / width);
                viewport.scrollLeft = 0;
                viewport.scrollTop = 0;
            };
            document.getElementById('zoom-fit').onclick = fit;
            var fitted = false;
            new ResizeObserver(function () {
                if (!fitted && viewport.clientWidth > 0) { fit(); fitted = true; }
            }).observe(viewport);
            viewport.addEventListener('wheel', function (event) {
                if (!event.ctrlKey) return;
                event.preventDefault();
                var rect = viewport.getBoundingClientRect();
                zoom(scale * Math.exp(-event.deltaY * 0.002), event.clientX - rect.left, event.clientY - rect.top);
            }, { passive: false });
            var drag = null;
            viewport.addEventListener('pointerdown', function (event) {
                if (event.button != 0) return;
                drag = { id: event.pointerId, x: event.clientX, y: event.clientY, left: viewport.scrollLeft, top: viewport.scrollTop };
                viewport.setPointerCapture(event.pointerId);
            });
            viewport.addEventListener('pointermove', function (event) {
                if (!drag) return;
                if (event.pointerId != drag.id) return;
                viewport.scrollLeft = drag.left + drag.x - event.clientX;
                viewport.scrollTop = drag.top + drag.y - event.clientY;
            });
            viewport.addEventListener('pointerup', function () { drag = null; });
            viewport.addEventListener('pointercancel', function () { drag = null; });
            viewport.addEventListener('lostpointercapture', function () { drag = null; });
        }());
        """;
}
