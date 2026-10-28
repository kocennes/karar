import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../../core/api/api_exception.dart';
import '../../../core/auth/age_gate.dart';
import '../../../core/theme/app_typography.dart';
import '../../../core/theme/karar_tokens.dart';
import '../../../shared/widgets/karar_button.dart';
import '../../../shared/widgets/karar_sheet.dart';
import 'auth_layout.dart';

/// P0-02 neutral date picker: it ends today (Turkish calendar date, as the
/// server counts) and opens with nothing selected (on the year grid), so it
/// does not lean toward an adult year.
Future<DateTime?> showBirthDatePicker(
  BuildContext context, {
  DateTime? selected,
}) {
  final today = AgeGate.today();
  return showDatePicker(
    context: context,
    initialDate: selected,
    currentDate: today,
    firstDate: DateTime(today.year - 120),
    lastDate: today,
    initialDatePickerMode: DatePickerMode.year,
    helpText: 'Doğum tarihini seç',
  );
}

/// Read-only birth-date field that opens [showBirthDatePicker].
class BirthDateField extends StatefulWidget {
  const BirthDateField({
    super.key,
    required this.value,
    required this.onChanged,
    this.enabled = true,
  });

  final DateTime? value;
  final ValueChanged<DateTime> onChanged;
  final bool enabled;

  @override
  State<BirthDateField> createState() => _BirthDateFieldState();
}

class _BirthDateFieldState extends State<BirthDateField> {
  final _controller = TextEditingController();

  @override
  void initState() {
    super.initState();
    _controller.text = _format(widget.value);
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  static String _format(DateTime? d) =>
      d == null ? '' : '${d.day}/${d.month}/${d.year}';

  Future<void> _pick() async {
    final picked = await showBirthDatePicker(context, selected: widget.value);
    if (picked == null || !mounted) return;
    _controller.text = _format(picked);
    widget.onChanged(picked);
  }

  @override
  Widget build(BuildContext context) {
    return TextFormField(
      controller: _controller,
      readOnly: true,
      enabled: widget.enabled,
      onTap: _pick,
      decoration: const InputDecoration(
        labelText: 'Doğum Tarihi',
        prefixIcon: Icon(Icons.calendar_today_rounded),
        hintText: 'Gün/Ay/Yıl',
      ),
      validator: (_) => widget.value == null ? 'Doğum tarihi gerekli.' : null,
    );
  }
}

/// P0-02 guest declaration (`AGE_DECLARATION_REQUIRED` on a vote): the same
/// neutral picker in a small sheet. [declare] sends the date; completes true
/// once it succeeded, false when dismissed or refused (the refusal itself is
/// shown by the caller once the sheet is gone).
Future<bool> showAgeDeclarationSheet(
  BuildContext context, {
  required Future<void> Function(DateTime dateOfBirth) declare,
}) async {
  final declared = await showKararSheet<bool>(
    context,
    scrollControlled: true,
    builder: (_) => _AgeDeclarationSheet(declare: declare),
  );
  return declared == true;
}

class _AgeDeclarationSheet extends StatefulWidget {
  const _AgeDeclarationSheet({required this.declare});

  final Future<void> Function(DateTime dateOfBirth) declare;

  @override
  State<_AgeDeclarationSheet> createState() => _AgeDeclarationSheetState();
}

class _AgeDeclarationSheetState extends State<_AgeDeclarationSheet> {
  final _formKey = GlobalKey<FormState>();
  DateTime? _dob;
  bool _busy = false;
  String? _error;

  Future<void> _submit() async {
    if (!_formKey.currentState!.validate()) return;
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await widget.declare(_dob!);
      if (mounted) Navigator.of(context).pop(true);
    } on ApiException catch (e) {
      if (!mounted) return;
      if (e.code == AgeGate.notEligibleCode) {
        Navigator.of(context).pop(false);
        return;
      }
      setState(() {
        _busy = false;
        _error = e.friendlyMessage;
      });
    } catch (_) {
      if (!mounted) return;
      setState(() {
        _busy = false;
        _error = 'Bir şey ters gitti. Tekrar dene.';
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return PopScope(
      canPop: !_busy,
      child: Form(
        key: _formKey,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            KararSheetHeader(
              title: 'Bir adım kaldı',
              onClose: _busy ? null : () => Navigator.of(context).pop(false),
            ),
            Padding(
              padding: const EdgeInsets.fromLTRB(20, 0, 20, 24),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Text(
                    AgeGate.birthDatePrompt,
                    style: AppTypography.body.copyWith(color: kc.text2),
                  ),
                  const SizedBox(height: 16),
                  BirthDateField(
                    value: _dob,
                    enabled: !_busy,
                    onChanged: (picked) => setState(() => _dob = picked),
                  ),
                  if (_error != null) ...[
                    const SizedBox(height: 12),
                    AuthErrorBox(message: _error!),
                  ],
                  const SizedBox(height: 20),
                  KararButton(
                    label: 'Devam et',
                    size: KararButtonSize.large,
                    onPressed: _busy ? null : _submit,
                    isLoading: _busy,
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// The refusal as a page ([ageRefusalPath]), for refusals outside the auth
/// screens (a write, a guest declaration).
class AgeRefusalScreen extends StatelessWidget {
  const AgeRefusalScreen({super.key});

  @override
  Widget build(BuildContext context) =>
      const AuthScaffold(child: AgeGateRefusal());
}

/// Neutral refusal: no reason, no threshold, no way back into the form.
class AgeGateRefusal extends StatelessWidget {
  const AgeGateRefusal({super.key});

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const AuthHeader(
          leading: AuthDisc(icon: Icons.info_outline_rounded),
          title: AgeGate.refusalTitle,
          subtitle: Text(AgeGate.refusalMessage),
        ),
        KararButton(
          label: 'Ana sayfaya dön',
          size: KararButtonSize.large,
          onPressed: () => context.go('/'),
        ),
      ],
    );
  }
}
