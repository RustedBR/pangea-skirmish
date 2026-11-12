mergeInto(LibraryManager.library, {
  // Copia texto para a área de transferência do navegador.
  // GUIUtility.systemCopyBuffer não funciona no WebGL: o Unity só escreve num buffer interno.
  // Tenta a API moderna; se o navegador recusar (sem gesto recente, http), cai no textarea +
  // execCommand; em último caso abre um prompt com o texto selecionado para Ctrl+C.
  PangeaCopyText: function (textPtr) {
    var text = UTF8ToString(textPtr);
    function legacy() {
      try {
        var ta = document.createElement('textarea');
        ta.value = text;
        ta.style.position = 'fixed'; ta.style.left = '-9999px'; ta.style.top = '0';
        document.body.appendChild(ta);
        ta.focus(); ta.select();
        var ok = document.execCommand('copy');
        document.body.removeChild(ta);
        if (!ok) window.prompt('Copie com Ctrl+C:', text);
      } catch (e) {
        try { window.prompt('Copie com Ctrl+C:', text); } catch (e2) {}
      }
    }
    try {
      if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(text).catch(legacy);
      } else legacy();
    } catch (e) { legacy(); }
  },

  // Baixa um arquivo de texto (log da partida) pelo navegador.
  PangeaDownloadText: function (namePtr, textPtr) {
    try {
      var name = UTF8ToString(namePtr);
      var text = UTF8ToString(textPtr);
      var blob = new Blob([text], { type: 'text/plain;charset=utf-8' });
      var url = URL.createObjectURL(blob);
      var a = document.createElement('a');
      a.href = url; a.download = name;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      setTimeout(function () { URL.revokeObjectURL(url); }, 2000);
    } catch (e) { console.error('PangeaDownloadText', e); }
  }
});
