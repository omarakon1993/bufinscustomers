/*!
 * bufins-markdown.js — Renderizador Markdown ligero y seguro para las respuestas de IA.
 *
 * Sustituye a los renderMarkdown() a base de regex que había duplicados en las vistas.
 * Soporta: encabezados, negrita/cursiva, `código` en línea, bloques ```código```,
 * listas (viñetas y numeradas, con anidación de 2 espacios), citas >, reglas ---,
 * enlaces [txt](url) (solo http/https/mailto/relativas) y TABLAS estilo GitHub.
 *
 * Seguro por construcción: TODO el texto se escapa primero (& < > "), y solo se
 * vuelven a emitir etiquetas de una lista blanca conocida. Los href se validan.
 *
 * API global:  window.bufinsMarkdown(texto) -> string HTML
 *              window.renderMarkdown        -> alias (si no existe ya)
 */
(function (global) {
    "use strict";

    function esc(s) {
        return String(s == null ? "" : s)
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;");
    }

    function safeHref(h) {
        var v = String(h || "").trim();
        return /^(https?:\/\/|mailto:|\/)/i.test(v) ? esc(v) : "#";
    }

    // Transformaciones "en línea". Recibe texto YA escapado.
    function inline(t) {
        return t
            .replace(/`([^`]+)`/g, function (_, c) {
                return '<code style="background:#f1eefe;border:1px solid #e0d9fb;border-radius:4px;padding:.05em .35em;font-size:.88em;">' + c + "</code>";
            })
            .replace(/\[([^\]]+)\]\(([^)\s]+)\)/g, function (_, txt, href) {
                return '<a href="' + safeHref(href) + '" target="_blank" rel="noopener noreferrer" style="color:#4a2de0;">' + txt + "</a>";
            })
            .replace(/\*\*([^*]+)\*\*/g, "<strong>$1</strong>")
            .replace(/__([^_]+)__/g, "<strong>$1</strong>")
            .replace(/(^|[^*])\*([^*\n]+)\*(?!\*)/g, "$1<em>$2</em>");
    }

    function splitRow(row) {
        return row.replace(/^\s*\|/, "").replace(/\|\s*$/, "").split("|").map(function (c) { return c.trim(); });
    }

    function renderTable(rows) {
        var head = splitRow(rows[0]);
        var body = rows.slice(2).map(splitRow);
        var h = '<div style="overflow-x:auto;margin:8px 0;">' +
                '<table style="border-collapse:collapse;width:100%;font-size:.9em;">' +
                '<thead><tr>';
        head.forEach(function (c) {
            h += '<th style="border:1px solid #e0d9fb;background:#f1eefe;padding:6px 10px;text-align:left;font-weight:600;">' + inline(esc(c)) + "</th>";
        });
        h += "</tr></thead><tbody>";
        body.forEach(function (cells) {
            h += "<tr>";
            head.forEach(function (_, idx) {
                h += '<td style="border:1px solid #ece8fb;padding:6px 10px;">' + inline(esc(cells[idx] || "")) + "</td>";
            });
            h += "</tr>";
        });
        return h + "</tbody></table></div>";
    }

    var RE_TABLE_SEP = /^\s*\|?[\s:]*-{1,}[\s:|-]*\|?\s*$/;

    function isSpecial(line, next) {
        return /^\s*```/.test(line) ||
               /^(#{1,6})\s+/.test(line) ||
               /^\s*([-*+]|\d+[.)])\s+/.test(line) ||
               /^\s*>\s?/.test(line) ||
               /^\s*([-*_])\1\1[-*_\s]*$/.test(line) ||
               (/\|/.test(line) && next != null && RE_TABLE_SEP.test(next) && /\|/.test(next));
    }

    function render(md) {
        if (!md) return "";
        var lines = String(md).replace(/\r\n?/g, "\n").split("\n");
        var out = [];
        var stack = []; // pila de tipos de lista abiertos: 'ul' | 'ol'
        var i = 0;

        function closeLists(toDepth) {
            while (stack.length > toDepth) out.push("</" + stack.pop() + ">");
        }

        while (i < lines.length) {
            var line = lines[i];
            var next = i + 1 < lines.length ? lines[i + 1] : null;

            // Bloque de código cercado
            var fence = line.match(/^\s*```(\w*)\s*$/);
            if (fence) {
                closeLists(0);
                var buf = [];
                i++;
                while (i < lines.length && !/^\s*```\s*$/.test(lines[i])) { buf.push(lines[i]); i++; }
                i++; // cierre
                out.push('<pre style="background:#160933;color:#e9e5ff;border-radius:8px;padding:12px 14px;overflow-x:auto;font-size:.86em;line-height:1.5;"><code>' + esc(buf.join("\n")) + "</code></pre>");
                continue;
            }

            // Tabla
            if (/\|/.test(line) && next != null && RE_TABLE_SEP.test(next) && /\|/.test(next)) {
                closeLists(0);
                var trows = [line, next];
                i += 2;
                while (i < lines.length && /\|/.test(lines[i]) && lines[i].trim() !== "") { trows.push(lines[i]); i++; }
                out.push(renderTable(trows));
                continue;
            }

            // Encabezado
            var hm = line.match(/^(#{1,6})\s+(.*)$/);
            if (hm) {
                closeLists(0);
                var lvl = Math.min(hm[1].length + 2, 6);
                var col = hm[1].length <= 1 ? "#160933" : "#583AFF";
                out.push("<h" + lvl + ' style="color:' + col + ";font-weight:700;margin:14px 0 6px;line-height:1.3;\">" + inline(esc(hm[2].trim())) + "</h" + lvl + ">");
                i++;
                continue;
            }

            // Regla horizontal
            if (/^\s*([-*_])\1\1[-*_\s]*$/.test(line)) {
                closeLists(0);
                out.push('<hr style="border:none;border-top:1px solid #e0d9fb;margin:14px 0;">');
                i++;
                continue;
            }

            // Cita
            if (/^\s*>\s?/.test(line)) {
                closeLists(0);
                var q = [];
                while (i < lines.length && /^\s*>\s?/.test(lines[i])) { q.push(lines[i].replace(/^\s*>\s?/, "")); i++; }
                out.push('<blockquote style="border-left:3px solid #c4b5fd;margin:8px 0;padding:2px 12px;color:#4b5563;">' + inline(esc(q.join(" "))) + "</blockquote>");
                continue;
            }

            // Elemento de lista
            var lm = line.match(/^(\s*)([-*+]|\d+[.)])\s+(.*)$/);
            if (lm) {
                var depth = Math.floor(lm[1].length / 2) + 1;
                var type = /\d/.test(lm[2]) ? "ol" : "ul";
                while (stack.length < depth) {
                    out.push("<" + type + ' style="margin:4px 0;padding-left:22px;">');
                    stack.push(type);
                }
                if (stack.length > depth) closeLists(depth);
                if (stack.length && stack[stack.length - 1] !== type) {
                    out.push("</" + stack.pop() + ">");
                    out.push("<" + type + ' style="margin:4px 0;padding-left:22px;">');
                    stack.push(type);
                }
                out.push('<li style="margin:2px 0;">' + inline(esc(lm[3])) + "</li>");
                i++;
                continue;
            }

            // Línea en blanco
            if (line.trim() === "") { closeLists(0); i++; continue; }

            // Párrafo
            closeLists(0);
            var para = [line];
            i++;
            while (i < lines.length && lines[i].trim() !== "" &&
                   !isSpecial(lines[i], i + 1 < lines.length ? lines[i + 1] : null)) {
                para.push(lines[i]);
                i++;
            }
            out.push('<p style="margin:0 0 8px;">' + inline(esc(para.join(" "))) + "</p>");
        }

        closeLists(0);
        return out.join("\n");
    }

    global.bufinsMarkdown = render;
    if (typeof global.renderMarkdown !== "function") global.renderMarkdown = render;
})(window);
