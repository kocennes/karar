import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../../core/auth/age_gate.dart';
import '../../../shared/widgets/karar_button.dart';
import 'auth_layout.dart';

/// P0-02 neutral date picker: it ends today and opens with nothing selected
/// (on the year grid), so it does not lean toward an adult year.
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
