import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_exception.dart';
import '../../core/providers.dart';
import '../../shared/widgets/centered_content.dart';
import '../../shared/widgets/karar_button.dart';

class FeedbackScreen extends ConsumerStatefulWidget {
  const FeedbackScreen({super.key});

  @override
  ConsumerState<FeedbackScreen> createState() => _FeedbackScreenState();
}

class _FeedbackScreenState extends ConsumerState<FeedbackScreen> {
  final _formKey = GlobalKey<FormState>();
  final _subjectController = TextEditingController();
  final _messageController = TextEditingController();
  final _emailController = TextEditingController();

  String _type = 'bug';
  bool _isSubmitting = false;

  @override
  void dispose() {
    _subjectController.dispose();
    _messageController.dispose();
    _emailController.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (!_formKey.currentState!.validate()) return;

    setState(() => _isSubmitting = true);
    try {
      await ref.read(authServiceProvider).submitFeedback(
            type: _type,
            subject: _subjectController.text.trim(),
            message: _messageController.text.trim(),
            contactEmail: _emailController.text.trim(),
          );

      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Geri bildirimin alındı. Teşekkürler.')),
      );
      Navigator.pop(context);
    } on ApiException catch (e) {
      _showError(e.friendlyMessage);
    } catch (_) {
      _showError('Geri bildirim gönderilemedi. Tekrar dene.');
    } finally {
      if (mounted) setState(() => _isSubmitting = false);
    }
  }

  void _showError(String message) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(message), behavior: SnackBarBehavior.floating),
    );
  }

  @override
  Widget build(BuildContext context) {
    final user = ref.watch(currentUserProvider);
    if (_emailController.text.isEmpty && user?.email.isNotEmpty == true) {
      _emailController.text = user!.email;
    }

    return PopScope(
      canPop: false,
      onPopInvokedWithResult: (didPop, result) async {
        if (didPop) return;

        if (_subjectController.text.isEmpty &&
            _messageController.text.isEmpty) {
          if (context.mounted) Navigator.pop(context);
          return;
        }

        final shouldPop = await showDialog<bool>(
          context: context,
          builder: (ctx) => AlertDialog(
            title: const Text('Vazgeçilsin mi?'),
            content: const Text(
                'Geri bildirimin kaydedilmeyecek. Çıkmak istiyor musun?'),
            actions: [
              TextButton(
                onPressed: () => Navigator.pop(ctx, false),
                child: const Text('Hayır'),
              ),
              FilledButton(
                onPressed: () => Navigator.pop(ctx, true),
                child: const Text('Evet'),
              ),
            ],
          ),
        );

        if (shouldPop == true && context.mounted) {
          Navigator.pop(context);
        }
      },
      child: Scaffold(
        appBar: AppBar(title: const Text('Hata bildir')),
        body: CenteredContent(
          child: ListView(
            padding: const EdgeInsets.all(16),
            children: [
              Form(
                key: _formKey,
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    SegmentedButton<String>(
                      segments: const [
                        ButtonSegment(
                          value: 'bug',
                          icon: Icon(Icons.bug_report_outlined),
                          label: Text('Hata'),
                        ),
                        ButtonSegment(
                          value: 'feedback',
                          icon: Icon(Icons.lightbulb_outline),
                          label: Text('Öneri'),
                        ),
                        ButtonSegment(
                          value: 'other',
                          icon: Icon(Icons.more_horiz),
                          label: Text('Diğer'),
                        ),
                      ],
                      selected: {_type},
                      onSelectionChanged: _isSubmitting
                          ? null
                          : (selection) =>
                              setState(() => _type = selection.first),
                    ),
                    const SizedBox(height: 16),
                    TextFormField(
                      controller: _subjectController,
                      enabled: !_isSubmitting,
                      textInputAction: TextInputAction.next,
                      maxLength: 120,
                      decoration: const InputDecoration(
                        labelText: 'Konu',
                        prefixIcon: Icon(Icons.subject_outlined),
                      ),
                      validator: (value) {
                        final text = value?.trim() ?? '';
                        if (text.length < 5) return 'En az 5 karakter yaz.';
                        return null;
                      },
                    ),
                    const SizedBox(height: 12),
                    TextFormField(
                      controller: _messageController,
                      enabled: !_isSubmitting,
                      minLines: 6,
                      maxLines: 10,
                      maxLength: 2000,
                      decoration: const InputDecoration(
                        labelText: 'Detay',
                        alignLabelWithHint: true,
                        prefixIcon: Icon(Icons.notes_outlined),
                      ),
                      validator: (value) {
                        final text = value?.trim() ?? '';
                        if (text.length < 10) return 'Biraz daha detay ekle.';
                        return null;
                      },
                    ),
                    const SizedBox(height: 12),
                    TextFormField(
                      controller: _emailController,
                      enabled: !_isSubmitting,
                      keyboardType: TextInputType.emailAddress,
                      decoration: const InputDecoration(
                        labelText: 'Dönüş e-postası (opsiyonel)',
                        prefixIcon: Icon(Icons.email_outlined),
                      ),
                    ),
                    const SizedBox(height: 20),
                    KararButton(
                      label: _isSubmitting ? 'Gönderiliyor...' : 'Gönder',
                      icon: Icons.send_outlined,
                      onPressed: _isSubmitting ? null : _submit,
                    ),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
