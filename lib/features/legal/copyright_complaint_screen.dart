import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../shared/widgets/centered_content.dart';
import '../../shared/widgets/karar_button.dart';

class CopyrightComplaintScreen extends StatefulWidget {
  const CopyrightComplaintScreen({super.key});

  @override
  State<CopyrightComplaintScreen> createState() =>
      _CopyrightComplaintScreenState();
}

class _CopyrightComplaintScreenState extends State<CopyrightComplaintScreen> {
  static const _copyrightEmail = 'telif@karar.app';

  final _formKey = GlobalKey<FormState>();
  final _nameController = TextEditingController();
  final _emailController = TextEditingController();
  final _contentUrlController = TextEditingController();
  final _workDescriptionController = TextEditingController();
  final _rightsStatementController = TextEditingController();

  bool _isSubmitting = false;

  @override
  void dispose() {
    _nameController.dispose();
    _emailController.dispose();
    _contentUrlController.dispose();
    _workDescriptionController.dispose();
    _rightsStatementController.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (!_formKey.currentState!.validate()) return;

    setState(() => _isSubmitting = true);
    final body = _buildEmailBody();
    final uri = Uri(
      scheme: 'mailto',
      path: _copyrightEmail,
      queryParameters: {
        'subject': 'FSEK telif şikayeti - içerik kaldırma talebi',
        'body': body,
      },
    );

    try {
      if (await canLaunchUrl(uri)) {
        await launchUrl(uri);
      } else {
        await Clipboard.setData(
          ClipboardData(text: '$_copyrightEmail\n\n$body'),
        );
        if (mounted) {
          ScaffoldMessenger.of(context).showSnackBar(
            const SnackBar(
              content: Text(
                  'Form metni kopyalandı. telif@karar.app adresine gönderebilirsin.'),
            ),
          );
        }
      }
    } finally {
      if (mounted) setState(() => _isSubmitting = false);
    }
  }

  String _buildEmailBody() {
    return [
      'Ad Soyad / Kurum: ${_nameController.text.trim()}',
      'Dönüş e-postası: ${_emailController.text.trim()}',
      'Şikayet edilen içerik bağlantısı: ${_contentUrlController.text.trim()}',
      '',
      'Eser / hak açıklaması:',
      _workDescriptionController.text.trim(),
      '',
      'Hak sahipliği beyanı:',
      _rightsStatementController.text.trim(),
      '',
      '72 saat içinde yanıt hedefinizi ve kaldırma talebimin sonucunu bu e-posta adresinden iletmenizi rica ederim.',
    ].join('\n');
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Telif / FSEK Şikayeti')),
      body: CenteredContent(
        child: ListView(
          padding: const EdgeInsets.all(16),
          children: [
            const _InfoBox(email: _copyrightEmail),
            const SizedBox(height: 20),
            Form(
              key: _formKey,
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  TextFormField(
                    controller: _nameController,
                    enabled: !_isSubmitting,
                    textInputAction: TextInputAction.next,
                    decoration: const InputDecoration(
                      labelText: 'Ad Soyad / Kurum',
                      prefixIcon: Icon(Icons.badge_outlined),
                    ),
                    validator: (value) {
                      final text = value?.trim() ?? '';
                      if (text.length < 3) {
                        return 'Ad veya kurum bilgisi gerekli.';
                      }
                      return null;
                    },
                  ),
                  const SizedBox(height: 12),
                  TextFormField(
                    controller: _emailController,
                    enabled: !_isSubmitting,
                    keyboardType: TextInputType.emailAddress,
                    textInputAction: TextInputAction.next,
                    decoration: const InputDecoration(
                      labelText: 'Dönüş e-postası',
                      prefixIcon: Icon(Icons.email_outlined),
                    ),
                    validator: (value) {
                      final text = value?.trim() ?? '';
                      if (!text.contains('@') || !text.contains('.')) {
                        return 'Geçerli bir e-posta yaz.';
                      }
                      return null;
                    },
                  ),
                  const SizedBox(height: 12),
                  TextFormField(
                    controller: _contentUrlController,
                    enabled: !_isSubmitting,
                    keyboardType: TextInputType.url,
                    textInputAction: TextInputAction.next,
                    decoration: const InputDecoration(
                      labelText: 'Şikayet edilen içerik bağlantısı',
                      prefixIcon: Icon(Icons.link_outlined),
                    ),
                    validator: (value) {
                      final text = value?.trim() ?? '';
                      if (!text.startsWith('http')) {
                        return 'İçerik bağlantısını tam URL olarak yaz.';
                      }
                      return null;
                    },
                  ),
                  const SizedBox(height: 12),
                  TextFormField(
                    controller: _workDescriptionController,
                    enabled: !_isSubmitting,
                    minLines: 4,
                    maxLines: 8,
                    maxLength: 1200,
                    decoration: const InputDecoration(
                      labelText: 'Eser / hak açıklaması',
                      alignLabelWithHint: true,
                      prefixIcon: Icon(Icons.description_outlined),
                    ),
                    validator: (value) {
                      final text = value?.trim() ?? '';
                      if (text.length < 20) {
                        return 'Talebi değerlendirmek için biraz daha detay ekle.';
                      }
                      return null;
                    },
                  ),
                  const SizedBox(height: 12),
                  TextFormField(
                    controller: _rightsStatementController,
                    enabled: !_isSubmitting,
                    minLines: 3,
                    maxLines: 6,
                    maxLength: 800,
                    decoration: const InputDecoration(
                      labelText: 'Hak sahipliği beyanı',
                      alignLabelWithHint: true,
                      prefixIcon: Icon(Icons.gavel_outlined),
                    ),
                    validator: (value) {
                      final text = value?.trim() ?? '';
                      if (text.length < 20) {
                        return 'Hak sahipliği beyanı gerekli.';
                      }
                      return null;
                    },
                  ),
                  const SizedBox(height: 20),
                  KararButton(
                    label: _isSubmitting
                        ? 'Hazırlanıyor...'
                        : 'E-posta Taslağı Oluştur',
                    icon: Icons.outgoing_mail,
                    onPressed: _isSubmitting ? null : _submit,
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

class _InfoBox extends StatelessWidget {
  const _InfoBox({required this.email});

  final String email;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: scheme.primaryContainer,
        borderRadius: BorderRadius.circular(8),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            'FSEK kapsamındaki telif hakkı şikayetleri $email adresinden alınır.',
            style: TextStyle(
              color: scheme.onPrimaryContainer,
              fontWeight: FontWeight.w700,
            ),
          ),
          const SizedBox(height: 8),
          Text(
            'Talebin doğru değerlendirilebilmesi için içerik bağlantısı, hak sahipliği beyanı ve eserin açıklaması gerekir. Hedef yanıt süresi 72 saattir.',
            style: TextStyle(
              color: scheme.onPrimaryContainer,
              height: 1.45,
            ),
          ),
        ],
      ),
    );
  }
}
