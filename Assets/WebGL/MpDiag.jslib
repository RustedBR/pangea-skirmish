mergeInto(LibraryManager.library, {
  MpDiagLogJS: function (msgPtr) {
    // Recebe uma string do C# (UTF-8 ponteiro) e repassa para window.MpDiagLog (index.html).
    try {
      var msg = UTF8ToString(msgPtr);
      if (typeof window !== 'undefined' && window.MpDiagLog) window.MpDiagLog(msg);
    } catch (e) {}
  }
});
