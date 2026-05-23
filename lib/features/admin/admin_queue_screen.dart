import 'package:flutter/material.dart';

import '../../core/theme/app_colors.dart';
import 'admin_service.dart';

class AdminQueueScreen extends StatefulWidget {
  const AdminQueueScreen({super.key, required this.adminService});
  final AdminService adminService;

  @override
  State<AdminQueueScreen> createState() => _AdminQueueScreenState();
}

class _AdminQueueScreenState extends State<AdminQueueScreen> {
  List<AdminQueueItem> _items = [];
  bool _loading = true;
  String? _error;
  String _priority = 'all';

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() { _loading = true; _error = null; });
    try {
      final result = await widget.adminService.fetchQueue(
        priority: _priority == 'all' ? null : _priority,
      );
      setState(() => _items = result.items);
    } catch (e) {
      setState(() => _error = e.toString());
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _action(AdminQueueItem item, String action) async {
    try {
      await widget.adminService.moderationAction(
        targetType: item.targetType,
        targetId: item.targetId,
        action: action,
      );
      setState(() => _items.remove(item));
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('İşlem uygulandı: $action')),
        );
      }
    } catch (_) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('İşlem başarısız.')),
        );
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: const Color(0xFF0F172A),
      appBar: AppBar(
        backgroundColor: const Color(0xFF1E293B),
        title: const Text('Moderasyon Kuyruğu', style: TextStyle(color: Colors.white)),
        actions: [
          DropdownButton<String>(
            value: _priority,
            dropdownColor: const Color(0xFF1E293B),
            style: const TextStyle(color: Colors.white),
            underline: const SizedBox(),
            items: const [
              DropdownMenuItem(value: 'all', child: Text('Tümü')),
              DropdownMenuItem(value: 'critical', child: Text('Kritik')),
              DropdownMenuItem(value: 'high', child: Text('Yüksek')),
              DropdownMenuItem(value: 'medium', child: Text('Orta')),
            ],
            onChanged: (v) {
              if (v != null) setState(() => _priority = v);
              _load();
            },
          ),
          IconButton(
            icon: const Icon(Icons.refresh, color: Colors.white70),
            onPressed: _load,
          ),
        ],
      ),
      body: _loading
          ? const Center(child: CircularProgressIndicator())
          : _error != null
              ? _ErrorView(error: _error!, onRetry: _load)
              : _items.isEmpty
                  ? const Center(
                      child: Text('Kuyruk boş.',
                          style: TextStyle(color: Colors.white54)),
                    )
                  : ListView.separated(
                      padding: const EdgeInsets.all(16),
                      itemCount: _items.length,
                      separatorBuilder: (_, __) => const SizedBox(height: 12),
                      itemBuilder: (_, i) => _QueueCard(
                        item: _items[i],
                        onAction: (a) => _action(_items[i], a),
                      ),
                    ),
    );
  }
}

class _QueueCard extends StatelessWidget {
  const _QueueCard({required this.item, required this.onAction});
  final AdminQueueItem item;
  final void Function(String) onAction;

  Color get _priorityColor => switch (item.priority) {
        'critical' => Colors.red,
        'high' => Colors.orange,
        'medium' => Colors.yellow,
        _ => Colors.green,
      };

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: const Color(0xFF1E293B),
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: Colors.white.withValues(alpha: 0.07)),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Container(
                padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
                decoration: BoxDecoration(
                  color: _priorityColor.withValues(alpha: 0.15),
                  borderRadius: BorderRadius.circular(4),
                ),
                child: Text(
                  item.priority.toUpperCase(),
                  style: TextStyle(color: _priorityColor, fontSize: 11, fontWeight: FontWeight.w700),
                ),
              ),
              const SizedBox(width: 8),
              Text(item.targetType,
                  style: const TextStyle(color: Colors.white54, fontSize: 12)),
              const Spacer(),
              Text('${item.reportCount} rapor',
                  style: const TextStyle(color: Colors.white38, fontSize: 12)),
            ],
          ),
          const SizedBox(height: 10),
          Text(
            item.content,
            style: const TextStyle(color: Colors.white, fontSize: 14, height: 1.4),
            maxLines: 4,
            overflow: TextOverflow.ellipsis,
          ),
          if (item.aiScore != null) ...[
            const SizedBox(height: 6),
            Text('AI skoru: ${(item.aiScore! * 100).toStringAsFixed(0)}%',
                style: TextStyle(
                    color: item.aiScore! > 0.7 ? Colors.red : Colors.white38,
                    fontSize: 12)),
          ],
          const SizedBox(height: 14),
          Row(
            children: [
              _ActionBtn(label: 'Onayla', color: AppColors.hakli,
                  onTap: () => onAction('approve')),
              const SizedBox(width: 8),
              _ActionBtn(label: 'Gizle', color: Colors.orange,
                  onTap: () => onAction('hide')),
              const SizedBox(width: 8),
              _ActionBtn(label: 'Sil', color: AppColors.haksiz,
                  onTap: () => onAction('delete')),
            ],
          ),
        ],
      ),
    );
  }
}

class _ActionBtn extends StatelessWidget {
  const _ActionBtn({required this.label, required this.color, required this.onTap});
  final String label;
  final Color color;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return OutlinedButton(
      onPressed: onTap,
      style: OutlinedButton.styleFrom(
        foregroundColor: color,
        side: BorderSide(color: color.withValues(alpha: 0.5)),
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
        minimumSize: Size.zero,
        tapTargetSize: MaterialTapTargetSize.shrinkWrap,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(6)),
      ),
      child: Text(label, style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w600)),
    );
  }
}

class _ErrorView extends StatelessWidget {
  const _ErrorView({required this.error, required this.onRetry});
  final String error;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Text(error, style: const TextStyle(color: Colors.white54)),
          const SizedBox(height: 12),
          TextButton(onPressed: onRetry, child: const Text('Yeniden Dene')),
        ],
      ),
    );
  }
}
