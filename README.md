# FLAFYO GAME TRANSLATOR — Phase 6

## الحالة الفعلية
**لم يتم تجميع أي كود ولم يُشغَّل أي اختبار بعد** (بيئة الكتابة بلا .NET/Windows). أول تحقق = تشغيل GitHub Actions.

| المكوّن | الحالة |
|---|---|
| Pipeline غير محجوب + اختباراته | مكتوب، غير مُختبر |
| التقاط منطقة من الشاشة (Thread مستقل، أولوية Below Normal) — GDI | مكتوب، غير مُختبر |
| OCR أوفلاين (Windows.Media.Ocr، إنجليزي) | مكتوب، غير مُختبر |
| ترجمة **أونلاين مؤقتة** (MyMemory) + قاموس ألعاب + Cache | مكتوب، غير مُختبر |
| Overlay شفاف لا يأخذ Focus ويمرّر الماوس + مخفي عن الالتقاط | مكتوب، غير مُختبر |
| واجهة (اختيار منطقة، سرعة، تشغيل/إيقاف) | مكتوبة، غير مُختبرة |
| حزمتان: خفيفة (OPUS-MT) وتقيلة (NLLB-200 600M) بتنزيل واختيار من الواجهة + بديل تلقائي لو فشلت | مكتوب، **غير مُختبر** (الأخطر: التقيلة، تُختبر في خطوة Real heavy translation test) |
| Beam search للخفيفة + تحويل المؤنث للمذكر + اصطلاحات إضافية | مكتوب، غير مُختبر (اختبارات وحدة + اختبار حقيقي في CI) |
| قاعدة اصطلاحات (idioms) تُعاد صياغتها قبل الترجمة + ملف المستخدم idioms.txt | مكتوب، غير مُختبر (اختبارات وحدة موجودة) |
| حفظ المنطقة والسرعة + اختصار Ctrl+Alt+T (RegisterHotKey، بدون Keyboard Hook) | مكتوب، غير مُختبر |
| Installer عبر Inno Setup (Install/Uninstall) + THIRD_PARTY_LICENSES.txt | مكتوب، غير مُختبر (خطوة CI اختيارية) |
| ترجمة **أوفلاين** بنموذج OPUS-MT en→ar (ONNX int8): Tokenizer + Greedy decoding + تنزيل الحزمة من الواجهة | مكتوب، **غير مُختبر** (يُختبر في CI بخطوة "Real offline translation test") |
| Windows Graphics Capture (للـ Fullscreen) | غير مبني |
| Installer، Profiles، SQLite، موقع | غير مبنية |
| Acceptance Test داخل لعبة | لم يُجرَ |

حدود v1: شاشة رئيسية واحدة، ويفضّل Windowed/Borderless. الجودة: OPUS-MT نموذج متوسط الحجم؛ نستخدم Greedy decoding حاليًا (Beam search لاحقًا). الجملة تُترجم كاملة لكن بدون ذاكرة للحوار السابق.
