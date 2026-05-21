import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { X } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Card, CardContent } from '@/components/ui/card'
import { useAssignRole } from '@/api/hooks/useIncidentMutations'
import type { Incident } from '@/api/types'
import { cn } from '@/lib/utils'

// Free-form UUID input until a /api/v1/users endpoint exists. The validator
// blocks malformed input client-side; the backend revalidates regardless.
const schema = z.object({ userId: z.string().uuid('Must be a valid UUID') })

type FormValues = z.infer<typeof schema>

type Role = 'Commander' | 'Communicator' | 'Responder'

interface RoleRowProps {
  role: Role
  current: string | null
  incidentId: string
}

function RoleRow({ role, current, incidentId }: RoleRowProps) {
  const assign = useAssignRole(incidentId)
  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { userId: '' },
  })

  const onSubmit = form.handleSubmit(values => {
    assign.mutate(
      { role, userId: values.userId },
      { onSuccess: () => form.reset() },
    )
  })

  const clear = () => assign.mutate({ role, userId: null })

  return (
    <div className="space-y-1.5">
      <div className="flex items-center justify-between">
        <p className="font-mono text-[10px] tracking-[0.18em] uppercase text-muted-foreground">
          {role}
        </p>
        <p
          className={cn(
            'font-mono text-[11px] tabular-nums',
            current ? 'text-foreground' : 'text-muted-foreground',
          )}
        >
          {current ? current.slice(0, 8) : '—'}
        </p>
      </div>
      <form onSubmit={onSubmit} className="flex items-center gap-1.5">
        <Input
          placeholder="user uuid"
          className="h-7 font-mono text-[11px]"
          aria-label={`Assign ${role}`}
          {...form.register('userId')}
        />
        <Button
          type="submit"
          size="sm"
          variant="secondary"
          className="h-7 px-2 font-mono text-[10px] uppercase tracking-wide"
          disabled={assign.isPending}
        >
          Assign
        </Button>
        {current !== null && (
          <Button
            type="button"
            size="icon"
            variant="ghost"
            className="h-7 w-7 text-muted-foreground hover:text-foreground"
            aria-label={`Clear ${role}`}
            onClick={clear}
            disabled={assign.isPending}
          >
            <X className="h-3 w-3" />
          </Button>
        )}
      </form>
      {form.formState.errors.userId && (
        <p className="font-mono text-[10px] text-red-500">
          {form.formState.errors.userId.message}
        </p>
      )}
    </div>
  )
}

interface RolesPanelProps {
  incident: Incident
}

export function RolesPanel({ incident }: RolesPanelProps) {
  return (
    <Card>
      <CardContent className="space-y-4 p-4">
        <p className="font-mono text-[10px] tracking-[0.18em] uppercase text-muted-foreground">
          Roles
        </p>
        <RoleRow role="Commander"    current={incident.commanderId}    incidentId={incident.id} />
        <RoleRow role="Communicator" current={incident.communicatorId} incidentId={incident.id} />
        <RoleRow role="Responder"    current={incident.responderId}    incidentId={incident.id} />
      </CardContent>
    </Card>
  )
}
