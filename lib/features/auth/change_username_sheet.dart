import 'dart:async';

import 'package:flutter/material.dart';

import '../../core/auth/auth_service.dart';
import '../../core/utils/validators.dart';

class ChangeUsernameSheet extends StatefulWidget {
  const ChangeUsernameSheet({
    super.key,
    required this.authService,
    required this.onSuccess,
  });

  final AuthService authService;
  final VoidCallback onSuccess;

  static Future<void> show(
    BuildContext context, {
    required AuthService authService,
    required VoidCallback onSuccess,
  }) =>
      showModalBottomSheet<void>(
        context: context,
        isDismissible: false,
        enableDrag: false,
        showDragHandle: true,
        isScrollControlled: true,
        builder: (_) => ChangeUsernameSheet(
          authService: authService,
          onSuccess: onSuccess,
        ),
      );

  @override
  State<ChangeUsernameSheet> createState() => _ChangeUsernameSheetState();
}

class _ChangeUsernameSheetState extends State<ChangeUsernameSheet> {
  final _controller = TextEditingController();
  Timer? _debounce;
  var _isLoading = false;
  var _isChecking = false;
  var _isAvailable = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _controller.text = widget.authService.currentUser?.username ?? '';
    _checkAvailability(_controller.text);
  }

  @override
  void dispose() {
    _debounce?.cancel();
    _controller.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    final name = _controller.text.trim().toLowerCase();
    final validationError = Validators.username(name);
    if (validationError != null) {
      setState(() => _error = validationError);
      return;
    }
    if (!_isAvailable) {
      setState(() => _error = 'Uygun bir kullanıcı adı seç.');
      return;
    }

    setState(() {
      _isLoading = true;
      _error = null;
    });

    try {
      await widget.authService.changeUsername(name);
      if (!mounted) return;
      Navigator.pop(context);
      widget.onSuccess();
    } catch (e) {
      setState(() => _error = 'Bu isim zaten alınmış veya geçersiz.');
    } finally {
      if (mounted) setState(() => _isLoading = false);
    }
  }

  void _onUsernameChanged(String value) {
    final normalized = value.trim().toLowerCase();
    if (value != normalized) {
      _controller.value = TextEditingValue(
        text: normalized,
        selection: TextSelection.collapsed(offset: normalized.length),
      );
      return;
    }

    _debounce?.cancel();
    setState(() {
      _error = null;
      _isAvailable = false;
      _isChecking = false;
    });

    final validationError = Validators.username(normalized);
    if (validationError != null) {
      setState(() => _error = validationError);
      return;
    }

    _debounce = Timer(
      const Duration(milliseconds: 350),
      () => _checkAvailability(normalized),
    );
  }

  Future<void> _checkAvailability(String value) async {
    final name = value.trim().toLowerCase();
    final validationError = Validators.username(name);
    if (validationError != null) {
      if (!mounted) return;
      setState(() {
        _error = validationError;
        _isAvailable = false;
        _isChecking = false;
      });
      return;
    }

    setState(() {
      _isChecking = true;
      _isAvailable = false;
      _error = null;
    });

    try {
      final available = await widget.authService.isUsernameAvailable(name);
      if (!mounted || _controller.text.trim().toLowerCase() != name) return;
      setState(() {
        _isAvailable = available;
        _error = available ? null : 'Bu kullanıcı adı alınmış.';
      });
    } catch (_) {
      if (!mounted) return;
      setState(() => _error = 'Uygunluk kontrol edilemedi. Tekrar dene.');
    } finally {
      if (mounted && _controller.text.trim().toLowerCase() == name) {
        setState(() => _isChecking = false);
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: EdgeInsets.fromLTRB(
        24,
        0,
        24,
        MediaQuery.of(context).viewInsets.bottom + 24,
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(
            'Kullanıcı adını seç',
            style: Theme.of(context).textTheme.headlineSmall?.copyWith(
                  fontWeight: FontWeight.bold,
                ),
          ),
          const SizedBox(height: 8),
          const Text(
            'Toplulukta bu isimle görüneceksin.',
          ),
          const SizedBox(height: 24),
          TextField(
            controller: _controller,
            maxLength: 20,
            onChanged: _onUsernameChanged,
            decoration: InputDecoration(
              labelText: 'Kullanıcı adı',
              errorText: _error,
              prefixText: '@',
              suffixIcon: _usernameSuffixIcon(),
            ),
            autofocus: true,
          ),
          if (_isAvailable) ...[
            const SizedBox(height: 4),
            Row(
              children: [
                Icon(
                  Icons.check_circle_outline,
                  size: 18,
                  color: Theme.of(context).colorScheme.primary,
                ),
                const SizedBox(width: 8),
                Text(
                  'Kullanıcı adı uygun',
                  style: TextStyle(
                    color: Theme.of(context).colorScheme.primary,
                    fontWeight: FontWeight.w600,
                  ),
                ),
              ],
            ),
          ],
          const SizedBox(height: 24),
          FilledButton(
            onPressed:
                _isLoading || _isChecking || !_isAvailable ? null : _submit,
            child: _isLoading
                ? const SizedBox(
                    width: 20,
                    height: 20,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  )
                : const Text('Devam Et'),
          ),
          TextButton(
            onPressed: _isLoading ? null : () => Navigator.pop(context),
            child: const Text('Şimdilik Geç'),
          ),
        ],
      ),
    );
  }

  Widget? _usernameSuffixIcon() {
    if (_isChecking) {
      return const Padding(
        padding: EdgeInsets.all(14),
        child: SizedBox(
          width: 18,
          height: 18,
          child: CircularProgressIndicator(strokeWidth: 2),
        ),
      );
    }
    if (_isAvailable) {
      return const Icon(Icons.check_circle_outline);
    }
    if (_error != null && _controller.text.trim().isNotEmpty) {
      return const Icon(Icons.error_outline);
    }
    return null;
  }
}
