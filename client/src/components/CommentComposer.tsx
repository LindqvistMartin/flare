import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Button } from '@/components/ui/button'
import { Textarea } from '@/components/ui/textarea'
import { useAddIncidentEvent } from '@/api/hooks/useIncidentMutations'

const schema = z.object({
  body: z.string().trim().min(1, 'Comment is empty').max(2000, 'Comment too long'),
})

type FormValues = z.infer<typeof schema>

interface CommentComposerProps {
  incidentId: string
}

export function CommentComposer({ incidentId }: CommentComposerProps) {
  const add = useAddIncidentEvent(incidentId)
  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { body: '' },
  })

  const onSubmit = form.handleSubmit(values => {
    // Backend whitelists CommentAdded + SeverityChanged; payload must parse as JSON.
    // Key matches the PostmortemDraftBuilder reader convention.
    add.mutate(
      {
        type: 'CommentAdded',
        payload: JSON.stringify({ comment: values.body.trim() }),
      },
      { onSuccess: () => form.reset() },
    )
  })

  const error = form.formState.errors.body?.message

  return (
    <form onSubmit={onSubmit} className="space-y-2">
      <label
        htmlFor={`composer-${incidentId}`}
        className="font-mono text-[10px] tracking-[0.18em] uppercase text-muted-foreground"
      >
        Add comment
      </label>
      <Textarea
        id={`composer-${incidentId}`}
        placeholder="Mitigation, observation, follow-up…"
        rows={3}
        className="resize-none font-mono text-sm"
        {...form.register('body')}
      />
      <div className="flex items-center justify-between">
        <p className="font-mono text-[10px] text-red-500" role="alert">
          {error ?? ' '}
        </p>
        <Button
          type="submit"
          size="sm"
          className="h-7 px-3 font-mono text-[10px] uppercase tracking-wide"
          disabled={add.isPending}
        >
          {add.isPending ? 'Posting…' : 'Post'}
        </Button>
      </div>
    </form>
  )
}
