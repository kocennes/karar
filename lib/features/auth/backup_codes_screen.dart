import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:go_router/go_router.dart';

import '../../shared/widgets/centered_content.dart';

class BackupCodesScreen extends StatefulWidget {
  const BackupCodesScreen({super.key, required this.codes});

  final List<String> codes;

  @override
  State<BackupCodesScreen> createState() => _BackupCodesScreenState();
}

class _BackupCodesScreenState extends State<BackupCodesScreen> {
  bool _confirmed = false;

  void _copyAll() {
    final text = widget.codes.join('\n');
    Clipboard.setData(ClipboardData(text: text));
    ScaffoldMessenger.of(context).showSnackBar(
      const SnackBar(content: Text('Yedek kodlar kopyalandı.')),
    );
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        leadingWidth: 130,
        leading: InkWell(
          onTap: () => context.go('/'),
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
            child: Image.asset('logo/logo.png', fit: BoxFit.contain),
          ),
        ),
        title: const Text('Yedek Kodlar'),
        centerTitle: true,
      ),
      body: SafeArea(
        child: CenteredContent(
          maxWidth: 400,
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                const Icon(Icons.lock_outline, size: 48),
                const SizedBox(height: 16),
                const Text(
                  'Bu kodları güvenli bir yerde saklayın.',
                  style: TextStyle(fontSize: 16, fontWeight: FontWeight.w600),
                  textAlign: TextAlign.center,
                ),
                const SizedBox(height: 8),
                const Text(
                  'Kimlik doğrulayıcı uygulamanıza erişemediğinizde bu kodlardan birini kullanabilirsiniz. Her kod yalnızca bir kez kullanılabilir.',
                  textAlign: TextAlign.center,
                ),
                const SizedBox(height: 24),
                GridView.count(
                  crossAxisCount: 2,
                  shrinkWrap: true,
                  physics: const NeverScrollableScrollPhysics(),
                  childAspectRatio: 3.5,
                  crossAxisSpacing: 12,
                  mainAxisSpacing: 12,
                  children: widget.codes
                      .map(
                        (code) => Container(
                          alignment: Alignment.center,
                          decoration: BoxDecoration(
                            color: Theme.of(context).colorScheme.surfaceContainerHighest,
                            borderRadius: BorderRadius.circular(8),
                          ),
                          child: Text(
                            code,
                            style: const TextStyle(
                              fontFamily: 'monospace',
                              fontWeight: FontWeight.bold,
                              letterSpacing: 2,
                            ),
                          ),
                        ),
                      )
                      .toList(),
                ),
                const SizedBox(height: 16),
                OutlinedButton.icon(
                  onPressed: _copyAll,
                  icon: const Icon(Icons.copy),
                  label: const Text('Tümünü Kopyala'),
                ),
                const Spacer(),
                CheckboxListTile(
                  value: _confirmed,
                  onChanged: (v) => setState(() => _confirmed = v ?? false),
                  title: const Text('Kodları güvenli bir yerde sakladım.'),
                  controlAffinity: ListTileControlAffinity.leading,
                  contentPadding: EdgeInsets.zero,
                ),
                const SizedBox(height: 12),
                FilledButton(
                  onPressed: _confirmed ? () => context.pop() : null,
                  style: FilledButton.styleFrom(
                    minimumSize: const Size.fromHeight(56),
                  ),
                  child: const Text('Tamam'),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

